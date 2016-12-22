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
            public EventStorePersistentSubscriptionBase Subscription { get; set; }
            public CancellationToken Token { get; set; }
            public int Bucket { get; set; }
        }

        private static readonly Histogram Acknowledging = Metric.Histogram("Acknowledged Events", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger("EventSubscriber");
        private static readonly TransportTransaction transportTranaction = new TransportTransaction();
        private static readonly ContextBag contextBag = new ContextBag();

        public static bool Live { get; set; }
        // Create a blocking collection based on ConcurrentQueue for ordering
        private static readonly Dictionary<int, ConcurrentQueue<ResolvedEvent>> ReadyEvents = new Dictionary<int, ConcurrentQueue<ResolvedEvent>>();
        
        private readonly List<Thread> _eventThreads;

        private ConcurrentBag<ResolvedEvent> _toBeAcknowledged;
        private Timer _acknowledger;

        private string _endpoint;
        private int _readsize;
        private bool _extraStats;

        private readonly MessageHandlerRegistry _registry;
        private readonly IEventStoreConnection[] _clients;
        private readonly JsonSerializerSettings _settings;
        private readonly MessageMetadataRegistry _messageMeta;

        private EventStorePersistentSubscriptionBase _subscription;

        private bool _disposed;

        public bool ProcessingLive => Live;
        public Action<string, Exception> Dropped { get; set; }

        public EventSubscriber(MessageHandlerRegistry registry, IMessageMapper mapper, MessageMetadataRegistry messageMeta, IEventStoreConnection[] connections)
        {
            _registry = registry;
            _clients = connections;
            _messageMeta = messageMeta;
            _eventThreads = new List<Thread>();
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };

            _toBeAcknowledged = new ConcurrentBag<ResolvedEvent>();
            _acknowledger = new Timer(_ =>
            {
                if (_toBeAcknowledged.IsEmpty) return;

                var newBag = new ConcurrentBag<ResolvedEvent>();
                var willAcknowledge = Interlocked.Exchange<ConcurrentBag<ResolvedEvent>>(ref _toBeAcknowledged, newBag);

                if (!ProcessingLive) return;

                Acknowledging.Update(willAcknowledge.Count);
                Logger.Write(LogLevel.Info, () => $"Acknowledging {willAcknowledge.Count} events");

                var page = 0;
                while (page < willAcknowledge.Count)
                {
                    var working = willAcknowledge.Skip(page).Take(1000);
                    _subscription.Acknowledge(working);
                    page += 1000;
                }

            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

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

            Logger.Write(LogLevel.Info, () => $"Endpoint [{_endpoint}] connecting to subscription group [{stream}]");

            var cancelSource = new CancellationTokenSource();

            Task.Run(async () =>
            {
                

                while (Bus.OnMessage == null || Bus.OnError == null)
                {
                    Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                    Thread.Sleep(1000);
                }

                foreach (var client in _clients)
                {
                    try
                    {
                        var settings = PersistentSubscriptionSettings.Create()
                            .StartFromBeginning()
                            .WithMaxRetriesOf(0)
                            .WithReadBatchOf(_readsize)
                            .WithLiveBufferSizeOf(_readsize)
                            .WithMessageTimeoutOf(TimeSpan.FromSeconds(30))
                            .CheckPointAfter(TimeSpan.FromSeconds(2))
                            .ResolveLinkTos()
                            .WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned);

                        await client.CreatePersistentSubscriptionAsync(stream, group, settings, client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                    }


                    _subscription =
                        await client.ConnectToPersistentSubscriptionAsync(stream, group, EventProcessor(cancelToken),
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
                            }, bufferSize: _readsize * 10, autoAck: false).ConfigureAwait(false);
                }
                // Create a new thread for pushing events
                // Another option is to use the thread pool with Task.Run however event-ordering is lost or at least severely degraded in the pool
                for (var i = 0; i < (Bus.PushSettings.MaxConcurrency / 2); i++)
                {
                    ReadyEvents[i] = new ConcurrentQueue<ResolvedEvent>();
                    var thread = new Thread((state) =>
                        {

                            var param = state as ThreadParam;
                            while (!param.Token.IsCancellationRequested)
                            {
                                ResolvedEvent e;
                                while (!ReadyEvents[param.Bucket].TryDequeue(out e))
                                {
                                    if (param.Token.IsCancellationRequested)
                                        return;
                                    Thread.Sleep(50);
                                }

                                var @event = e.Event;

                                var descriptor = @event.Metadata.Deserialize(_settings);

                                var headers = new Dictionary<string, string>(descriptor.Headers)
                                {
                                    [Headers.EnclosedMessageTypes] =
                                    SerializeEnclosedMessageTypes(Type.GetType(@event.EventType)),
                                    [Headers.MessageId] = @event.EventId.ToString()
                                };

                                Logger.Write(LogLevel.Debug,
                                    () =>
                                            $"Processing event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");
                                using (var tokenSource = new CancellationTokenSource())
                                {
                                    var processed = false;
                                    var numberOfDeliveryAttempts = 0;

                                    while (!processed)
                                    {
                                        if (param.Token.IsCancellationRequested)
                                            return;

                                        try
                                        {
                                            // Don't re-use the event id for the message id
                                            var messageContext = new MessageContext(Guid.NewGuid().ToString(),
                                                headers,
                                                @event.Data ?? new byte[0], transportTranaction, tokenSource,
                                                contextBag);
                                            Bus.OnMessage(messageContext).ConfigureAwait(false).GetAwaiter().GetResult();
                                            processed = true;
                                        }
                                        catch (ObjectDisposedException)
                                        {
                                            // NSB transport has been disconnected
                                            param.Subscription.Stop(TimeSpan.FromMinutes(1));
                                            return;
                                        }
                                        catch (Exception ex)
                                        {
                                            ++numberOfDeliveryAttempts;
                                            var errorContext = new ErrorContext(ex, headers,
                                                @event.EventId.ToString(),
                                                @event.Data ?? new byte[0], transportTranaction,
                                                numberOfDeliveryAttempts);
                                            if (Bus.OnError(errorContext).ConfigureAwait(false).GetAwaiter().GetResult() ==
                                                ErrorHandleResult.Handled)
                                                break;

                                        }
                                    }


                                    // If user decided to cancel receive - don't schedule an ACK
                                    if (tokenSource.IsCancellationRequested)
                                        continue;

                                    Logger.Write(LogLevel.Debug,
                                        () => $"Queueing acknowledge for event {@event.EventId}");
                                    _toBeAcknowledged.Add(e);
                                    //subscription.Acknowledge(e);
                                }
                            }

                        })
                    { IsBackground = true, Name = $"Event Thread {i}" };
                    thread.Start(new ThreadParam { Bucket = i, Subscription = _subscription, Token = cancelSource.Token });
                    _eventThreads.Add(thread);
                }
                Live = true;
            });
            return Task.CompletedTask;
        }


        private Action<EventStorePersistentSubscriptionBase, ResolvedEvent> EventProcessor(CancellationToken token)
        {
            // Entrypoint from eventstore client API
            return (subscription, e) =>
            {
                var @event = e.Event;
                if (!@event.IsJson)
                {
                    _toBeAcknowledged.Add(e);
                    return;
                }
                Logger.Write(LogLevel.Debug,
                    () => $"Received event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");

                if (token.IsCancellationRequested)
                {
                    subscription.Stop(TimeSpan.FromMinutes(1));
                    return;
                }

                var bucket = Math.Abs(@event.EventStreamId.GetHashCode() % (Bus.PushSettings.MaxConcurrency / 2));
                ReadyEvents[bucket].Enqueue(e);
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _subscription.Stop(TimeSpan.FromMinutes(1));
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
