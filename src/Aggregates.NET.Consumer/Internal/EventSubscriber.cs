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
        private static readonly Histogram Acknowledging = Metric.Histogram("Acknowledged Events", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventSubscriber));
        private static readonly TransportTransaction transportTranaction = new TransportTransaction();
        private static readonly ContextBag contextBag = new ContextBag();

        private ConcurrentBag<ResolvedEvent> _toBeAcknowledged;
        private Timer _acknowledger;

        private string _endpoint;
        private int _readsize;
        private bool _extraStats;

        private SemaphoreSlim _concurrencyLimit;

        private readonly MessageHandlerRegistry _registry;
        private readonly IEventStoreConnection _connection;
        private readonly JsonSerializerSettings _settings;
        private readonly MessageMetadataRegistry _messageMeta;

        private EventStorePersistentSubscriptionBase _subscription;

        private bool _disposed;

        public bool ProcessingLive { get; set; }
        public Action<string, Exception> Dropped { get; set; }

        public EventSubscriber(MessageHandlerRegistry registry,
            IEventStoreConnection connection, IMessageMapper mapper, MessageMetadataRegistry messageMeta)
        {
            _registry = registry;
            _connection = connection;
            _messageMeta = messageMeta;
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
                    .WithReadBatchOf(_readsize)
                    .WithMaxRetriesOf(0)
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

                // PushSettings will only exist AFTER finding OnMessage 
                if (_concurrencyLimit == null)
                    _concurrencyLimit = new SemaphoreSlim(Bus.PushSettings.MaxConcurrency);

                _subscription = _connection.ConnectToPersistentSubscriptionAsync(stream, group, EventProcessor(cancelToken), subscriptionDropped: (sub, reason, e) =>
                {
                    Logger.Write(LogLevel.Warn, () => $"Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
                    ProcessingLive = false;
                    Dropped?.Invoke(reason.ToString(), e);
                }, bufferSize: _readsize * 10, autoAck: false).Result;
                ProcessingLive = true;
            });

            
        }

        private Action<EventStorePersistentSubscriptionBase, ResolvedEvent> EventProcessor(CancellationToken token)
        {
            return (subscription, e) =>
            {
                var @event = e.Event;
                if (!@event.IsJson)
                {
                    _toBeAcknowledged.Add(e);
                    return;
                }
                Logger.Write(LogLevel.Debug,
                    () => $"Event {@event.EventId} type {@event.EventType} appeared stream [{@event.EventStreamId}] number {@event.EventNumber} Position C:{e.OriginalPosition?.CommitPosition}/P:{e.OriginalPosition?.PreparePosition}");


                var descriptor = @event.Metadata.Deserialize(_settings);

                if (token.IsCancellationRequested)
                {
                    subscription.Stop(TimeSpan.FromSeconds(30));
                    return;
                }
                _concurrencyLimit.WaitAsync(token).ConfigureAwait(false).GetAwaiter().GetResult();

                Task.Run(async () =>
                {

                    var headers = new Dictionary<string, string>(descriptor.Headers)
                    {
                        [Headers.EnclosedMessageTypes] = SerializeEnclosedMessageTypes(Type.GetType(@event.EventType)),
                        [Headers.MessageId] = @event.EventId.ToString()
                    };

                    Logger.Write(LogLevel.Debug, () => $"Processing event {@event.EventId}");
                    using (var tokenSource = new CancellationTokenSource())
                    {
                        var processed = false;
                        var numberOfDeliveryAttempts = 0;

                        while (!processed)
                        {
                            try
                            {
                                var messageContext = new MessageContext(@event.EventId.ToString(), headers,
                                    @event.Data ?? new byte[0], transportTranaction, tokenSource, contextBag);
                                await Bus.OnMessage(messageContext).ConfigureAwait(false);
                                processed = true;
                            }
                            catch (ObjectDisposedException)
                            {
                                // Rabbit has been disconnected
                                subscription.Stop(TimeSpan.FromSeconds(30));
                                return;
                            }
                            catch (Exception ex)
                            {
                                ++numberOfDeliveryAttempts;
                                var errorContext = new ErrorContext(ex, headers, @event.EventId.ToString(),
                                    @event.Data ?? new byte[0], transportTranaction, numberOfDeliveryAttempts);
                                if (await Bus.OnError(errorContext).ConfigureAwait(false) == ErrorHandleResult.Handled)
                                    break;

                                _concurrencyLimit.Release();
                                await
                                    Task.Delay(100*(numberOfDeliveryAttempts/2), cancellationToken: token)
                                        .ConfigureAwait(false);
                                await _concurrencyLimit.WaitAsync(token).ConfigureAwait(false);
                            }
                        }

                        _concurrencyLimit.Release();

                        if (tokenSource.IsCancellationRequested)
                            return;

                        Logger.Write(LogLevel.Debug, () => $"Queueing acknowledge for event {@event.EventId}");
                        _toBeAcknowledged.Add(e);
                        //subscription.Acknowledge(e);
                    }
                }, token);
            };
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _concurrencyLimit.Dispose();
            _subscription.Stop(TimeSpan.FromSeconds(30));
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
