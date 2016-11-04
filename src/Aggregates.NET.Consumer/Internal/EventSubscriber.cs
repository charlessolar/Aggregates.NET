using System;
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
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Transport;
using NServiceBus.Unicast;
using MessageContext = NServiceBus.Transport.MessageContext;

namespace Aggregates.Internal
{
    internal class EventSubscriber : IEventSubscriber, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventSubscriber));
        private static readonly TransportTransaction transportTranaction = new TransportTransaction();
        private static readonly ContextBag contextBag = new ContextBag();

        private string _endpoint;
        private int _readsize;

        private readonly SemaphoreSlim _concurrencyLimit;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private readonly MessageHandlerRegistry _registry;
        private readonly IEventStoreConnection _connection;
        private readonly JsonSerializerSettings _settings;

        private readonly Func<MessageContext, Task> _onMessage;
        private readonly Func<ErrorContext, Task<ErrorHandleResult>> _onError;
        private readonly PushSettings _pushSettings;

        private EventStorePersistentSubscriptionBase _subscription;

        private bool _disposed;

        public bool ProcessingLive { get; set; }
        public Action<string, Exception> Dropped { get; set; }

        public EventSubscriber(IPushMessages messagePusher, MessageHandlerRegistry registry,
            IEventStoreConnection connection, IMessageMapper mapper)
        {
            _registry = registry;
            _connection = connection;
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };
            // Todo: make configurable
            _concurrencyLimit = new SemaphoreSlim(5);
            _cancellationTokenSource = new CancellationTokenSource();


            // We want eventstore to push message directly into NSB
            // That way we can have all the fun stuff like retries, error queues, incoming/outgoing mutators etc
            // But NSB doesn't have a way to just insert a message directly into the pipeline
            // You can SendLocal which will send the event out to the transport then back again but in addition to being a
            // waste of time its not safe incase this instance goes down because we'd have ACKed the event now sitting 
            // unprocessed on the instance specific queue.
            //
            // The only way I can find to call into the pipeline directly is to highjack these private fields on
            // the RabbitMq transport message pump.
            // Yes other transports are possible I just don't have a reason to support them (yet)

            var msgPushType = messagePusher.GetType();
            var property = msgPushType.GetField("onMessage", BindingFlags.Instance | BindingFlags.NonPublic);
            if (property == null)
                throw new ArgumentException(
                    "Could not start event subscriber - RabbitMQ is required for Aggregates.NET at this time");

            _onMessage = (Func<MessageContext, Task>)property.GetValue(messagePusher);
            property = msgPushType.GetField("onError", BindingFlags.Instance | BindingFlags.NonPublic);
            if (property == null)
                throw new ArgumentException(
                    "Could not start event subscriber - RabbitMQ is required for Aggregates.NET at this time");

            _onError = (Func<ErrorContext, Task<ErrorHandleResult>>)property.GetValue(messagePusher);
            property = msgPushType.GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic);
            if (property == null)
                throw new ArgumentException(
                    "Could not start event subscriber - RabbitMQ is required for Aggregates.NET at this time");

            _pushSettings = (PushSettings)property.GetValue(messagePusher);
        }

        public async Task Setup(string endpoint, int readsize)
        {
            _endpoint = endpoint;
            _readsize = readsize;

            if (!_connection.Settings.GossipSeeds.Any())
                throw new ArgumentException(
                    "Eventstore connection settings does not contain gossip seeds (even if single host call SetGossipSeedEndPoints and SetClusterGossipPort)");
        
            var manager = new ProjectionsManager(_connection.Settings.Log,
                new IPEndPoint(_connection.Settings.GossipSeeds[0].EndPoint.Address, _connection.Settings.ExternalGossipPort), TimeSpan.FromSeconds(5));

            var discoveredEvents = _registry.GetMessageTypes().Where(x => typeof(IEvent).IsAssignableFrom(x)).ToList();

            var stream = $"{_endpoint}.{Assembly.GetExecutingAssembly().GetName().Version}";

            // Link all events we are subscribing to to a stream
            var functions =
                discoveredEvents
                    .Select(eventType => $"'{eventType.AssemblyQualifiedName}': function(s,e) {{ linkTo('{stream}', e); }}")
                    .Aggregate((cur, next) => $"{cur},\n{next}");

            var definition = $"fromAll().when({{\n{functions}\n}})";

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
                // Projection doesn't exist 
                await manager.CreateContinuousAsync(stream, definition, _connection.Settings.DefaultUserCredentials).ConfigureAwait(false);
            }
        }

        public async Task Subscribe()
        {
            var stream = $"{_endpoint}.{Assembly.GetExecutingAssembly().GetName().Version}";

            Logger.Write(LogLevel.Info, () => $"Endpoint [{_endpoint}] connecting to subscription group [{stream}]");

            try
            {
                var settings = PersistentSubscriptionSettings.Create()
                    .WithLiveBufferSizeOf(_readsize)
                    .StartFromCurrent()
                    .WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned)
                    .Build();
                await
                    _connection.CreatePersistentSubscriptionAsync(stream, stream, settings,
                        _connection.Settings.DefaultUserCredentials).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }

            _subscription = await _connection.ConnectToPersistentSubscriptionAsync(stream, stream, (subscription, e) =>
            {
                Logger.Write(LogLevel.Debug, () => $"Event appeared position {e.OriginalPosition?.CommitPosition}");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;
                
                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _settings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;
                if (_cancellationTokenSource.IsCancellationRequested) return;

                _concurrencyLimit.Wait(_cancellationTokenSource.Token);

                Task.Run(async () =>
                {
                    var headers = new Dictionary<string, string>(descriptor.Headers);

                    using (var tokenSource = new CancellationTokenSource())
                    {
                        var processed = false;
                        var errorHandled = false;
                        var numberOfDeliveryAttempts = 0;

                        while (!processed && !errorHandled)
                        {
                            try
                            {
                                var messageContext = new MessageContext(e.Event.EventId.ToString(), headers,
                                    e.Event.Data ?? new byte[0], transportTranaction, tokenSource, contextBag);
                                await _onMessage(messageContext).ConfigureAwait(false);
                                processed = true;
                            }
                            catch (Exception ex)
                            {
                                ++numberOfDeliveryAttempts;
                                var errorContext = new ErrorContext(ex, headers, e.Event.EventId.ToString(),
                                    e.Event.Data ?? new byte[0], transportTranaction, numberOfDeliveryAttempts);
                                errorHandled = await _onError(errorContext).ConfigureAwait(false) ==
                                               ErrorHandleResult.Handled;
                            }
                        }

                        if (processed && !tokenSource.IsCancellationRequested)
                            subscription.Acknowledge(e);
                        _concurrencyLimit.Release();
                    }
                }, _cancellationTokenSource.Token);

            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.Write(LogLevel.Warn, () => $"Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
                ProcessingLive = false;
                Dropped?.Invoke(reason.ToString(), e);
            }, autoAck: false).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellationTokenSource.Cancel();
            _concurrencyLimit.Dispose();
            _subscription.Stop(TimeSpan.FromSeconds(5));
        }
    }
}
