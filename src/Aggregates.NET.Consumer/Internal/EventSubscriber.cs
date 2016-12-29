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
            public SemaphoreSlim InFlight { get; set; }
            public JsonSerializerSettings Settings { get; set; }
            public int Bucket { get; set; }
        }

        private static readonly Histogram Acknowledging = Metric.Histogram("Acknowledged Events", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger("EventSubscriber");

        public static bool Live { get; set; }

        private string _endpoint;
        private int _readsize;
        private bool _extraStats;

        private readonly MessageHandlerRegistry _registry;
        private readonly JsonSerializerSettings _settings;
        private readonly MessageMetadataRegistry _messageMeta;

        private readonly IEventStoreConnection[] _clients;
        private readonly List<EventStorePersistentSubscriptionBase> _subscriptions;

        private bool _disposed;

        public bool ProcessingLive => Live;
        public Action<string, Exception> Dropped { get; set; }

        public EventSubscriber(MessageHandlerRegistry registry, IMessageMapper mapper,
            MessageMetadataRegistry messageMeta, IEventStoreConnection[] connections)
        {
            _registry = registry;
            _clients = connections;
            _messageMeta = messageMeta;
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };
            _subscriptions = new List<EventStorePersistentSubscriptionBase>();
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
                            eventType => $"'{eventType.AssemblyQualifiedName}': processEvent")
                        .Aggregate((cur, next) => $"{cur},\n{next}");

                var definition = $@"
function processEvent(s,e) {{
    var stream = e.streamId;
    if(stream.substr(stream.indexOf('.') + 1, 3) === 'OOB')
        linkTo('OOB.{stream}', e);
    else
        linkTo('{stream}', e);
}}
fromAll().when({{
{functions}
}});";

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
                            manager.CreateContinuousAsync(stream, definition, false,
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
            var pinnedGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.PINNED";
            var roundRobinGroup = $"{_endpoint}.{Assembly.GetEntryAssembly().GetName().Version}.ROUND";




            Task.Run(async () =>
            {

                while (Bus.OnMessage == null || Bus.OnError == null)
                {
                    Logger.Warn($"Could not find NSBs onMessage handler yet - if this persists there is a problem.");
                    Thread.Sleep(1000);
                }

                foreach (var client in _clients)
                {

                    var settings = PersistentSubscriptionSettings.Create()
                        .StartFromBeginning()
                        .WithMaxRetriesOf(0)
                        .WithReadBatchOf(_readsize)
                        .WithLiveBufferSizeOf(_readsize)
                        .WithMessageTimeoutOf(TimeSpan.FromMinutes(5))
                        .CheckPointAfter(TimeSpan.FromSeconds(2))
                        .ResolveLinkTos()
                        .WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned);
                    try
                    {
                        await
                            client.CreatePersistentSubscriptionAsync(stream, pinnedGroup, settings,
                                client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                        Logger.Info($"Created PINNED persistent subscription to stream [{stream}]");

                    }
                    catch (InvalidOperationException) { }
                    try
                    {

                        settings.WithNamedConsumerStrategy(SystemConsumerStrategies.RoundRobin);
                        await
                            client.CreatePersistentSubscriptionAsync($"OOB.{stream}", roundRobinGroup, settings,
                                client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                        Logger.Info($"Created ROUND ROBIN persistent subscription to stream [{stream}]");
                    }
                    catch (InvalidOperationException) { }



                    // Todo: with autoAck on ES will assume once the event is delivered it will be processed
                    // This is OK for us because if the event fails it will be moved onto the error queue and doesn't need to be handled by ES
                    // But in reality if this instance crashes with 100 events waiting in memory those events will be lost.  
                    // So something better is needed.  
                    // The problem comes with manually acking if ES doesn't receive an ACK within a timeframe it retries the message.  When the problem
                    // is just that the consumers are too backed up.  Causing a double event
                    Logger.Write(LogLevel.Info,
                        () =>
                                $"Endpoint [{_endpoint}] connecting {Bus.PushSettings.MaxConcurrency} times to PINNED subscription group [{pinnedGroup}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");

                    for (var i = 0; i < 2; i++)
                    {
                        var pinnedCancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
                        _subscriptions.Add(
                            await client.ConnectToPersistentSubscriptionAsync(stream, pinnedGroup,
                                EventProcessor(pinnedCancelSource.Token),
                                subscriptionDropped: (sub, reason, e) =>
                                {
                                    Logger.Write(LogLevel.Warn,
                                        () =>
                                                $"Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
                                    
                                    pinnedCancelSource.Cancel();
                                    _subscriptions.Remove(sub);
                                }, bufferSize: _readsize, autoAck: true).ConfigureAwait(false));
                    }

                    Logger.Write(LogLevel.Info,
                        () =>
                                $"Endpoint [{_endpoint}] connecting to ROUND ROBIN subscription group [{roundRobinGroup}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");

                    // OOB events processed single threaded so they dont overtake main event processing
                    var rrCancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
                    _subscriptions.Add(
                        await client.ConnectToPersistentSubscriptionAsync($"OOB.{stream}", roundRobinGroup, PooledEventProcessor(rrCancelSource.Token),
                            subscriptionDropped: (sub, reason, e) =>
                            {
                                Logger.Write(LogLevel.Warn,
                                    () =>
                                            $"Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
                                
                                rrCancelSource.Cancel();
                                _subscriptions.Remove(sub);
                            }, bufferSize: _readsize, autoAck: true).ConfigureAwait(false));
                }

            });

            return Task.CompletedTask;
        }

        private async Task ProcessEvent(CancellationToken token, RecordedEvent @event)
        {
            var transportTransaction = new TransportTransaction();
            var contextBag = new ContextBag();

            var descriptor = @event.Metadata.Deserialize(_settings);

            var messageId = Guid.NewGuid().ToString();
            var headers = new Dictionary<string, string>(descriptor.Headers)
            {
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                [Headers.EnclosedMessageTypes] = SerializeEnclosedMessageTypes(Type.GetType(@event.EventType)),
                [Headers.MessageId] = messageId,
                ["EventId"] = @event.EventId.ToString()
            };

            using (var tokenSource = new CancellationTokenSource())
            {
                var processed = false;
                var numberOfDeliveryAttempts = 0;

                while (!processed && !token.IsCancellationRequested)
                {
                    try
                    {
                        // Don't re-use the event id for the message id
                        var messageContext = new MessageContext(messageId,
                            headers,
                            @event.Data ?? new byte[0], transportTransaction, tokenSource,
                            contextBag);
                        await Bus.OnMessage(messageContext).ConfigureAwait(false);
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
                            @event.Data ?? new byte[0], transportTransaction,
                            numberOfDeliveryAttempts);
                        if (await Bus.OnError(errorContext).ConfigureAwait(false) ==
                            ErrorHandleResult.Handled)
                            break;
                        try
                        {
                            await Task.Delay(numberOfDeliveryAttempts*250, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { }
                    }
                }
            }
        }

        private Action<EventStorePersistentSubscriptionBase, ResolvedEvent> PooledEventProcessor(CancellationToken token)
        {
            var limited = new SemaphoreSlim(Bus.PushSettings.MaxConcurrency, Bus.PushSettings.MaxConcurrency);
            // Entrypoint from eventstore client API
            return (subscription, e) =>
            {
                var @event = e.Event;
                if (!@event.IsJson)
                    return;

                try
                {
                    limited.Wait(token);
                }
                catch (OperationCanceledException)
                {
                    subscription.Stop(TimeSpan.FromMinutes(1));
                    return;
                }
                Logger.Write(LogLevel.Debug,
                    () =>
                            $"Processing OOB event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");


                Task.Run(async () =>
                {
                    await ProcessEvent(token, @event).ConfigureAwait(false);
                    limited.Release();
                    
                }, token);
                
            };
        }
        private Action<EventStorePersistentSubscriptionBase, ResolvedEvent> EventProcessor(CancellationToken token)
        {
            // Entrypoint from eventstore client API
            return (subscription, e) =>
            {
                var @event = e.Event;
                if (!@event.IsJson)
                    return;
                
                Logger.Write(LogLevel.Debug,
                    () =>
                            $"Processing event {@event.EventId} type {@event.EventType} stream [{@event.EventStreamId}] number {@event.EventNumber}");

                ProcessEvent(token, @event).ConfigureAwait(false).GetAwaiter().GetResult();
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
