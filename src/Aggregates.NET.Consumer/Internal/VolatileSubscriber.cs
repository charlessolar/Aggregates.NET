using System;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System.Threading;
using NServiceBus.Settings;
using Aggregates.Exceptions;
using NServiceBus.MessageInterfaces;

namespace Aggregates.Internal
{
    public class VolatileSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(VolatileSubscriber));
        private readonly IBuilder _builder;
        private readonly IEventStoreConnection _client;
        private readonly IDispatcher _dispatcher;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;

        public Boolean ProcessingLive { get; set; }
        public Action<String, Exception> Dropped { get; set; }

        public VolatileSubscriber(IBuilder builder, IEventStoreConnection client, IDispatcher dispatcher, ReadOnlySettings settings, IMessageMapper mapper)
        {
            _builder = builder;
            _client = client;
            _dispatcher = dispatcher;
            _settings = settings;
            _jsonSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };
        }

        public void SubscribeToAll(String endpoint)
        {
            var readSize = _settings.Get<Int32>("ReadSize");
            Logger.InfoFormat("Endpoint '{0}' subscribing to all events from END", endpoint);

            var settings = new CatchUpSubscriptionSettings(readSize * readSize, readSize, false, false);
            _client.SubscribeToAllFrom(Position.End, settings, (subscription, e) =>
            {
                Logger.DebugFormat("Event appeared position {0}", e.OriginalPosition?.CommitPosition);
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position)
                if (data == null) return;

                try
                {
                    _dispatcher.Dispatch(data, descriptor, e.OriginalPosition?.CommitPosition);
                }
                catch (SubscriptionCanceled)
                {
                    subscription.Stop();
                    throw;
                }

            }, liveProcessingStarted: (_) =>
            {
                Logger.Info("Live processing started");
                ProcessingLive = true;
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e);
                ProcessingLive = false;
                if (Dropped != null)
                    Dropped.Invoke(reason.ToString(), e);
            });
        }
    }
}