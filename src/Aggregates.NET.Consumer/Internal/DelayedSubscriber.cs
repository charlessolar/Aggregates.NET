using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.Projections;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Transport;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;

namespace Aggregates.Internal
{
    class DelayedSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger("DelaySubscriber");
        private static readonly ILog SlowLogger = LogManager.GetLogger("Slow Alarm");

        private static readonly Metrics.Timer DelayedExecution = Metric.Timer("Delayed Execution", Unit.Items, tags: "debug");
        private static readonly Histogram DelayedHandled = Metric.Histogram("Delayed Handled", Unit.Items, tags: "debug");


        private class ThreadParam
        {
            public DelayedClient[] Clients { get; set; }
            public CancellationToken Token { get; set; }
            public JsonSerializerSettings JsonSettings { get; set; }
        }
        private Thread _delayedThread;
        private CancellationTokenSource _cancelation;
        private string _endpoint;
        private int _readsize;
        private bool _extraStats;

        private readonly int _maxDelayed;
        private readonly JsonSerializerSettings _settings;

        private readonly IEventStoreConnection[] _clients;

        private bool _disposed;

        public DelayedSubscriber( IMessageMapper mapper,
             IEventStoreConnection[] connections, int maxDelayed)
        {
            _clients = connections;
            _maxDelayed = maxDelayed;
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };
        }


        public async Task Setup(string endpoint, int readsize, bool extraStats)
        {
            _endpoint = endpoint;
            _readsize = readsize;
            _extraStats = extraStats;

            foreach (var client in _clients)
            {
                if (client.Settings.GossipSeeds == null || !client.Settings.GossipSeeds.Any())
                    throw new ArgumentException(
                        "Eventstore connection settings does not contain gossip seeds (even if single host call SetGossipSeedEndPoints and SetClusterGossipPort)");

                var manager = new ProjectionsManager(client.Settings.Log,
                    new IPEndPoint(client.Settings.GossipSeeds[0].EndPoint.Address,
                        client.Settings.ExternalGossipPort), TimeSpan.FromSeconds(5));

                await manager.EnableAsync("$by_category", client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                
            }
        }

        public Task Subscribe(CancellationToken cancelToken)
        {
            _cancelation = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
            var stream = $"$ce-{_endpoint}.{StreamTypes.Delayed}";
            var group = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.{StreamTypes.Delayed}.ROUND";

            Task.Run(async () =>
            {

                while (Bus.OnMessage == null || Bus.OnError == null)
                {
                    Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                    Thread.Sleep(1000);
                }


                var settings = PersistentSubscriptionSettings.Create()
                    .StartFromBeginning()
                    .WithMaxRetriesOf(10)
                    .WithReadBatchOf(_readsize)
                    .WithLiveBufferSizeOf(_readsize * _readsize)
                    .WithMessageTimeoutOf(TimeSpan.FromMilliseconds(int.MaxValue))
                    .CheckPointAfter(TimeSpan.FromSeconds(30))
                    .MaximumCheckPointCountOf(_readsize * _readsize)
                    .ResolveLinkTos()
                    .WithNamedConsumerStrategy(SystemConsumerStrategies.RoundRobin); 
                if (_extraStats)
                    settings.WithExtraStatistics();

                var clients = new DelayedClient[_clients.Count()];

                for (var i = 0; i < _clients.Count(); i++)
                {
                    var client = _clients.ElementAt(i);

                    var clientCancelSource = CancellationTokenSource.CreateLinkedTokenSource(_cancelation.Token);

                    client.Closed += (object s, ClientClosedEventArgs args) =>
                    {
                        Logger.Info($"Eventstore disconnected - shutting down delayed subscription");
                        clientCancelSource.Cancel();
                    };
                    try
                    {
                        await
                            client.CreatePersistentSubscriptionAsync(stream, group, settings,
                                client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                        Logger.Info($"Created ROUND ROBIN persistent subscription to stream [{stream}]");

                    }
                    catch (InvalidOperationException)
                    {
                    }

                    clients[i] = new DelayedClient(client, stream, group, _maxDelayed, clientCancelSource.Token);
                }
                _delayedThread = new Thread(Threaded)
                { IsBackground = true, Name = $"Delayed Event Thread" };
                _delayedThread.Start(new ThreadParam { Token = cancelToken, Clients = clients, JsonSettings = _settings });

            });

            return Task.CompletedTask;
        }

        private static void Threaded(object state)
        {
            var param = (ThreadParam)state;

            param.Clients.SelectAsync(x => x.Connect()).Wait();

            var threadIdle = Metric.Timer("Delayed Events Idle", Unit.None, tags: "debug");

            TimerContext? idleContext = threadIdle.NewContext();
            while (true)
            {
                param.Token.ThrowIfCancellationRequested();

                var noEvents = true;
                for (var i = 0; i < param.Clients.Count(); i++)
                {
                    var client = param.Clients.ElementAt(i);
                    var events = client.Flush();

                    if (!events.Any())
                        continue;

                    noEvents = false;
                    idleContext?.Dispose();
                    idleContext = null;

                    var delayed = events.Select(x => x.Event.Data.Deserialize<DelayedMessage>(param.JsonSettings)).ToArray();

                    Logger.Write(LogLevel.Debug, () => $"Processing {delayed.Count()} bulk events");

                    var transportTransaction = new TransportTransaction();
                    var contextBag = new ContextBag();
                    // Hack to get all the delayed messages to bulk invoker without NSB deserializing and processing each one
                    contextBag.Set(Defaults.BulkHeader, delayed.AsEnumerable());

                    var messageId = Guid.NewGuid().ToString();
                    var headers = new Dictionary<string, string>()
                    {
                        [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                        [Headers.MessageId] = messageId,
                        [Defaults.BulkHeader] = delayed.Count().ToString(),
                    };

                    // Run bulk process on this thread
                    using (var tokenSource = new CancellationTokenSource())
                    {
                        var processed = false;
                        var numberOfDeliveryAttempts = 0;
                        using (var ctx = DelayedExecution.NewContext())
                        {
                            while (!processed)
                            {
                                try
                                {
                                    // If canceled, this will throw the number of time immediate retry requires to send the message to the error queue
                                    param.Token.ThrowIfCancellationRequested();

                                    // Don't re-use the event id for the message id
                                    var messageContext = new NServiceBus.Transport.MessageContext(messageId,
                                        headers,
                                        new byte[0], transportTransaction, tokenSource,
                                        contextBag);
                                    Bus.OnMessage(messageContext).Wait(param.Token);
                                    processed = true;
                                }
                                catch (ObjectDisposedException)
                                {
                                    // NSB transport has been disconnected
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    ++numberOfDeliveryAttempts;
                                    var errorContext = new ErrorContext(ex, headers,
                                        messageId,
                                        new byte[0], transportTransaction,
                                        numberOfDeliveryAttempts);
                                    if (Bus.OnError(errorContext).Result ==
                                        ErrorHandleResult.Handled)
                                        break;

                                }
                            }
                            if(ctx.Elapsed > TimeSpan.FromSeconds(5))
                                SlowLogger.Warn($"Processing {delayed.Count()} bulked events took {ctx.Elapsed.TotalSeconds} seconds!");
                        }
                    }
                    Logger.Write(LogLevel.Debug,
                           () => $"Scheduling acknowledge of {delayed.Count()} bulk events");
                    DelayedHandled.Update(delayed.Count());
                    client.Acknowledge(events);
                }
                if (idleContext == null)
                    idleContext = threadIdle.NewContext();
                // Cheap hack to not burn cpu incase there are no events
                if (noEvents) Thread.Sleep(50);
            }


        }
        

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancelation.Cancel();
            _delayedThread.Join();
        }
        
    }
}
