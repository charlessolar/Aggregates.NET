using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
using NServiceBus.Pipeline;
using NServiceBus.Transport;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;
using MessageContext = NServiceBus.Transport.MessageContext;
using Timer = System.Threading.Timer;

namespace Aggregates.Internal
{
    internal class EventSubscriber : IEventSubscriber
    {
        private class ThreadParam
        {
            public CancellationToken Token { get; set; }
            public int Bucket { get; set; }
        }

        private static readonly Histogram Acknowledging = Metric.Histogram("Acknowledged Events", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger("EventSubscriber");
        private static readonly TransportTransaction transportTransaction = new TransportTransaction();
        private static readonly ContextBag contextBag = new ContextBag();

        private static readonly Dictionary<int, ConcurrentQueue<Tuple<int, ResolvedEvent>>> ReadyEvents = new Dictionary<int, ConcurrentQueue<Tuple<int, ResolvedEvent>>>();
        public static bool Live { get; set; }

        private readonly List<Thread> _eventThreads;
        
        private string _endpoint;
        private int _readsize;
        private bool _extraStats;
        private SemaphoreSlim _inflight;

        private readonly MessageHandlerRegistry _registry;
        private readonly JsonSerializerSettings _settings;
        private readonly MessageMetadataRegistry _messageMeta;

        private readonly IEventStoreConnection[] _clients;
        private EventStorePersistentSubscriptionBase[] _subscriptions;

        private bool _disposed;

        public bool ProcessingLive => Live;
        public Action<string, Exception> Dropped { get; set; }

        public EventSubscriber(MessageHandlerRegistry registry, IMessageMapper mapper,
            MessageMetadataRegistry messageMeta, IEventStoreConnection[] connections, int inflight)
        {
            _registry = registry;
            _clients = connections;
            _messageMeta = messageMeta;
            _eventThreads = new List<Thread>();
            _inflight = new SemaphoreSlim(inflight);
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

                var discoveredEvents =
                    _registry.GetMessageTypes().Where(x => typeof(IEvent).IsAssignableFrom(x)).ToList();

                var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}";

                // Link all events we are subscribing to to a stream
                var functions =
                    discoveredEvents
                        .Select(
                            eventType =>
                                    $"'{eventType.AssemblyQualifiedName}': function(s,e) {{ linkTo('{stream}', e); return null; }}")
                        .Aggregate((cur, next) => $"{cur},\n{next}");

                var definition = $"fromAll().when({{\n{functions}\n}});";

                try
                {
                    var existing = await manager.GetQueryAsync(stream).ConfigureAwait(false);

                    if (existing != definition)
                    {
                        Logger.Fatal(
                            $"Projection [{stream}] already exists and is a different version!  If you've upgraded your code don't forget to bump your app's version!");
                        throw new EndpointVersionException(
                            $"Projection [{stream}] already exists and is a different version!  If you've upgraded your code don't forget to bump your app's version!");
                    }
                }
                catch (ProjectionCommandFailedException)
                {
                    try
                    {
                        // Projection doesn't exist 
                        await
                            manager.CreateContinuousAsync(stream, definition,
                                    client.Settings.DefaultUserCredentials)
                                .ConfigureAwait(false);
                    }
                    catch (ProjectionCommandFailedException)
                    {
                    }
                }
            }
        }

        public Task Subscribe(CancellationToken cancelToken)
        {
            var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}";
            var group = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.sub";


            var cancelSource = new CancellationTokenSource();
            

            Task.Run(async () =>
            {
                var subscriptions = new List<Task<EventStorePersistentSubscriptionBase>>();

                while (Bus.OnMessage == null || Bus.OnError == null)
                {
                    Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                    Thread.Sleep(1000);
                }

                var count = 0;
                foreach (var client in _clients)
                {

                    try
                    {
                        var settings = PersistentSubscriptionSettings.Create()
                            .StartFromBeginning()
                            .WithMaxRetriesOf(0)
                            .WithReadBatchOf(_readsize)
                            .WithLiveBufferSizeOf(_readsize)
                            .CheckPointAfter(TimeSpan.FromSeconds(2))
                            .ResolveLinkTos()
                            .WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned);

                        await
                            client.CreatePersistentSubscriptionAsync(stream, group, settings,
                                client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                        Logger.Info($"Created persistent subscription to stream [{stream}]");
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    // Todo: with autoAck on ES will assume once the event is delivered it will be processed
                    // This is OK for us because if the event fails it will be moved onto the error queue and doesn't need to be handled by ES
                    // But in reality if this instance crashes with 100 events waiting in memory those events will be lost.  
                    // So something better is needed.  
                    // The problem comes with manually acking if ES doesn't receive an ACK within a timeframe it retries the message.  When the problem
                    // is just that the consumers are too backed up.  Causing a double event
                    Logger.Write(LogLevel.Info, () => $"Endpoint [{_endpoint}] connecting to subscription group [{group}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");
                    subscriptions.Add(
                        client.ConnectToPersistentSubscriptionAsync(stream, group, EventProcessor(count, cancelToken),
                            subscriptionDropped: (sub, reason, e) =>
                            {
                                Logger.Write(LogLevel.Warn,
                                    () =>
                                            $"Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
                                Live = false;
                                cancelSource.Cancel();
                                _eventThreads.ForEach(x => x.Join());
                                _eventThreads.Clear();
                                Dropped?.Invoke(reason.ToString(), e);
                            }, bufferSize: _readsize * 10, autoAck: true));
                    count++;
                }
                await Task.WhenAll(subscriptions).ConfigureAwait(false);
                _subscriptions = subscriptions.Select(x => x.Result).ToArray();


                // Create a new thread for pushing events
                // Another option is to use the thread pool with Task.Run however event-ordering is lost or at least severely degraded in the pool
                for (var i = 0; i < (Bus.PushSettings.MaxConcurrency / 2); i++)
                {
                    ReadyEvents[i] = new ConcurrentQueue<Tuple<int, ResolvedEvent>>();
                    var thread = new Thread((state) =>
                    {

                        var param = state as ThreadParam;
                        while (!param.Token.IsCancellationRequested)
                        {
                            Tuple<int, ResolvedEvent> e;
                            while (!ReadyEvents[param.Bucket].TryDequeue(out e))
                            {
                                if (param.Token.IsCancellationRequested)
                                    return;
                                Thread.Sleep(50);
                            }

                            var @event = e.Item2.Event;

                            var descriptor = @event.Metadata.Deserialize(_settings);

                            var headers = new Dictionary<string, string>(descriptor.Headers)
                            {
                                [Headers.EnclosedMessageTypes] = SerializeEnclosedMessageTypes(Type.GetType(@event.EventType)),
                                [Headers.MessageId] = @event.EventId.ToString()
                            };

                            Logger.Write(LogLevel.Debug,
                                () => $"Processing event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");
                            using (var tokenSource = new CancellationTokenSource())
                            {
                                var processed = false;
                                var numberOfDeliveryAttempts = 0;

                                while (!processed)
                                {
                                    if (param.Token.IsCancellationRequested)
                                        return;
                                    var messageId = Guid.NewGuid().ToString();
                                    try
                                    {
                                        // Don't re-use the event id for the message id
                                        var messageContext = new MessageContext(messageId,
                                        headers,
                                        @event.Data ?? new byte[0], transportTransaction, tokenSource,
                                        contextBag);
                                        Bus.OnMessage(messageContext).ConfigureAwait(false).GetAwaiter().GetResult();
                                        processed = true;
                                    }
                                    catch (ObjectDisposedException)
                                    {
                                        // NSB transport has been disconnected
                                        return;
                                    }
                                    catch (Exception ex)
                                    {
                                        ++numberOfDeliveryAttempts;
                                        var errorContext = new ErrorContext(ex, headers,
                                            messageId,
                                            @event.Data ?? new byte[0], transportTransaction,
                                            numberOfDeliveryAttempts);
                                        if (Bus.OnError(errorContext).ConfigureAwait(false).GetAwaiter().GetResult() ==
                                            ErrorHandleResult.Handled)
                                            break;

                                    }
                                }

                            }
                            _inflight.Release();
                        }

                    })
                    { IsBackground = true, Name = $"Event Thread {i}" };
                    thread.Start(new ThreadParam { Bucket = i, Token = cancelSource.Token });
                    _eventThreads.Add(thread);
                }
                Live = true;
            });

            return Task.CompletedTask;
        }


        private Action<EventStorePersistentSubscriptionBase, ResolvedEvent> EventProcessor(int index, CancellationToken token)
        {
            // Entrypoint from eventstore client API
            return (subscription, e) =>
            {
                var @event = e.Event;
                if (!@event.IsJson)
                    return;
                
                Logger.Write(LogLevel.Debug,
                    () => $"Received event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");

                if (token.IsCancellationRequested)
                {
                    subscription.Stop(TimeSpan.FromMinutes(1));
                    return;
                }

                _inflight.Wait(token);

                var bucket = Math.Abs(@event.EventStreamId.GetHashCode() % (Bus.PushSettings.MaxConcurrency / 2));
                ReadyEvents[bucket].Enqueue(new Tuple<int, ResolvedEvent>(index, e));

            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var sub in _subscriptions)
                sub.Stop(TimeSpan.FromMinutes(1));
        }

        string SerializeEnclosedMessageTypes(Type messageType)
        {
            var metadata = _messageMeta.GetMessageMetadata(messageType);

            var assemblyQualifiedNames = new HashSet<string>();
            foreach (var type in metadata.MessageHierarchy)
            {
                assemblyQualifiedNames.Add(type.AssemblyQualifiedName);
            }

            return string.Join(";", assemblyQualifiedNames);
        }
    }
}
