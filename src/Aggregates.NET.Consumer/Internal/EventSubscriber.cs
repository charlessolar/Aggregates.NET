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
        // Todo: lets come up with a more efficient collection
        private static readonly Dictionary<string, Tuple<DateTime, IList<ResolvedEvent>>> WaitingEvents = new Dictionary<string, Tuple<DateTime, IList<ResolvedEvent>>>();
        private static readonly object WaitingLock = new object();

        // Moves now in-order events to ReadyEvents
        private static readonly Timer MoveToReady = new Timer(_ =>
        {
            if (!Live) return;

            List<KeyValuePair<string, Tuple<DateTime, IList<ResolvedEvent>>>> ready;
            lock (WaitingLock)
            {
                ready = WaitingEvents.Where(x => x.Value.Item1 < DateTime.UtcNow).ToList();
                foreach (var stream in ready)
                    WaitingEvents.Remove(stream.Key);
            }

            var seenEvents = new HashSet<Guid>();
            foreach (var stream in ready)
            {
                var bucket = Math.Abs(stream.Key.GetHashCode() % Bus.PushSettings.MaxConcurrency);
                var events = stream.Value.Item2.OrderBy(x => x.Event.EventNumber);
                foreach (var @event in events)
                {
                    if (seenEvents.Contains(@event.Event.EventId))
                        continue;
                    seenEvents.Add(@event.Event.EventId);

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Readying event {@event.Event.EventId} type {@event.Event.EventType} stream [{@event.Event.EventStreamId}] number {@event.Event.EventNumber} to bucket {bucket}");
                    ReadyEvents[bucket].Enqueue(@event);
                }
            }


        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        private readonly List<Thread> _eventThreads;

        private ConcurrentBag<ResolvedEvent> _toBeAcknowledged;
        private Timer _acknowledger;

        private string _endpoint;
        private int _readsize;
        private bool _extraStats;

        private readonly MessageHandlerRegistry _registry;
        private readonly IEventStoreConnection _connection;
        private readonly JsonSerializerSettings _settings;
        private readonly MessageMetadataRegistry _messageMeta;

        private EventStorePersistentSubscriptionBase _subscription;

        private bool _disposed;

        public bool ProcessingLive => Live;
        public Action<string, Exception> Dropped { get; set; }

        public EventSubscriber(MessageHandlerRegistry registry,
            IEventStoreConnection connection, IMessageMapper mapper, MessageMetadataRegistry messageMeta)
        {
            _registry = registry;
            _connection = connection;
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

            if (_connection.Settings.GossipSeeds == null || !_connection.Settings.GossipSeeds.Any())
                throw new ArgumentException(
                    "Eventstore connection settings does not contain gossip seeds (even if single host call SetGossipSeedEndPoints and SetClusterGossipPort)");

            var manager = new ProjectionsManager(_connection.Settings.Log,
                new IPEndPoint(_connection.Settings.GossipSeeds[0].EndPoint.Address, _connection.Settings.ExternalGossipPort), TimeSpan.FromSeconds(5));

            // We use this system projection - so enable it
            await manager.EnableAsync("$by_event_type", _connection.Settings.DefaultUserCredentials).ConfigureAwait(false);

            var discoveredEvents = _registry.GetMessageTypes().Where(x => typeof(IEvent).IsAssignableFrom(x)).ToList();

            var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}";

            // Link all events we are subscribing to to a stream
            var functions =
                discoveredEvents
                    .Select(eventType => $"'{eventType.AssemblyQualifiedName}': function(s,e) {{ linkTo('{stream}', e); }}")
                    .Aggregate((cur, next) => $"{cur},\n{next}");
            var eventTypes =
                discoveredEvents
                    .Select(eventType => $"'$et-{eventType.AssemblyQualifiedName}'")
                    .Aggregate((cur, next) => $"{cur},{next}");

            var definition = $"fromStreams([{eventTypes}]).when({{\n{functions}\n}});";

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
                        manager.CreateContinuousAsync(stream, definition, _connection.Settings.DefaultUserCredentials)
                            .ConfigureAwait(false);
                }
                catch (ProjectionCommandConflictException)
                {
                }
            }
        }

        public async Task Subscribe(CancellationToken cancelToken)
        {
            var stream = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}";
            var group = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.sub";

            Logger.Write(LogLevel.Info, () => $"Endpoint [{_endpoint}] connecting to subscription group [{stream}]");

            try
            {
                var settings = PersistentSubscriptionSettings.Create()
                    .StartFromBeginning()
                    .WithMaxRetriesOf(0)
                    .WithReadBatchOf(_readsize)
                    .WithLiveBufferSizeOf(_readsize)
                    .WithMessageTimeoutOf(TimeSpan.FromSeconds(60))
                    .CheckPointAfter(TimeSpan.FromSeconds(10))
                    .ResolveLinkTos()
                    .WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned);

                await
                    _connection.CreatePersistentSubscriptionAsync(stream, group, settings,
                        _connection.Settings.DefaultUserCredentials).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {

                while (Bus.OnMessage == null || Bus.OnError == null)
                {
                    Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                    Thread.Sleep(1000);
                }

                var cancelSource = new CancellationTokenSource();

                _subscription = _connection.ConnectToPersistentSubscriptionAsync(stream, group, EventProcessor(cancelToken), subscriptionDropped: (sub, reason, e) =>
                {
                    Logger.Write(LogLevel.Warn, () => $"Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
                    cancelSource.Cancel();
                    _eventThreads.ForEach(x => x.Join());
                    _eventThreads.Clear();
                    Live = false;
                    Dropped?.Invoke(reason.ToString(), e);
                }, bufferSize: _readsize * 10, autoAck: false).Result;

                // Create a new thread for pushing events
                // Another option is to use the thread pool with Task.Run however event-ordering is lost or at least severely degraded in the pool
                for (var i = 0; i < Bus.PushSettings.MaxConcurrency; i++)
                {
                    ReadyEvents[i] = new ConcurrentQueue<ResolvedEvent>();
                    var thread = new Thread((state) =>
                        {

                            var param = state as ThreadParam;
                            while (!param.Token.IsCancellationRequested)
                            {
                                ResolvedEvent e;
                                while (!ReadyEvents[param.Bucket].TryDequeue(out e))
                                    Thread.Sleep(50);

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
                                        try
                                        {
                                            var messageContext = new MessageContext(@event.EventId.ToString(),
                                                headers,
                                                @event.Data ?? new byte[0], transportTranaction, tokenSource,
                                                contextBag);
                                            Bus.OnMessage(messageContext).ConfigureAwait(false).GetAwaiter().GetResult();
                                            processed = true;
                                        }
                                        catch (ObjectDisposedException)
                                        {
                                            // Rabbit has been disconnected
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
                                        return;

                                    Logger.Write(LogLevel.Debug,
                                        () => $"Queueing acknowledge for event {@event.EventId}");
                                    _toBeAcknowledged.Add(e);
                                    //subscription.Acknowledge(e);
                                }
                            }

                        })
                    { IsBackground = true, Name = $"Event Thread {i}" };
                    thread.Start(new ThreadParam { Bucket=i, Subscription = _subscription, Token = cancelSource.Token });
                    _eventThreads.Add(thread);
                }
                Live = true;
            });


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

                // Instead of processing event right this moment, put it into a delayed "queue"
                // The event will be delayed from running for ~5 seconds - should be enough time to get more of the same stream
                // Issue from ES is events are not in order.  Meaning for a single stream "XYZ" I'll get events in this order: 1, 5, 0, 3, 2, 4
                // If we process those as they come in, the user will be confused since they'll want 0, 1, 2, 3, 4, 5
                // We can't GUARENTEE ordering - but we can come pretty close by just adding a 5 second delay 
                // When we receive event 1 we'll start the countdown, we'll then receive 5, 0, 3, 2, and 4
                // Once 5 seconds are up we'll have all 6 events and we'll be able to process them in order!

                lock (WaitingLock)
                {
                    if (!WaitingEvents.ContainsKey(@event.EventStreamId))
                        WaitingEvents[@event.EventStreamId] =
                            new Tuple<DateTime, IList<ResolvedEvent>>(DateTime.UtcNow + TimeSpan.FromSeconds(5),
                                new List<ResolvedEvent>());

                    WaitingEvents[@event.EventStreamId].Item2.Add(e);
                }
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
