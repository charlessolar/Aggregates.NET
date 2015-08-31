using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class DurableSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(DurableSubscriber));
        private readonly IEventStoreConnection _client;
        private readonly IPersistCheckpoints _store;
        private readonly JsonSerializerSettings _settings;

        public DurableSubscriber(IEventStoreConnection client, IPersistCheckpoints store, JsonSerializerSettings settings)
        {
            _client = client;
            _store = store;
            _settings = settings;
        }

        public void SubscribeToAll(String endpoint, IDispatcher dispatcher)
        {
            var saved = _store.Load(endpoint);

            Logger.DebugFormat("Endpoint '{0}' subscribing to all events from position '{1}'", endpoint, saved);

            _client.SubscribeToAllFrom(saved, false, (_, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _settings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;

                try
                {
                    dispatcher.Dispatch(data);
                }
                catch (Exception ex)
                {
                    Logger.ErrorFormat("Error processing events, stopping consumer.  Exception: {0}", ex);
                    _.Stop();
                }

                if (e.OriginalPosition.HasValue)
                    _store.Save(endpoint, e.OriginalPosition.Value);
            }, liveProcessingStarted: (_) =>
            {
                Logger.Info("Live processing started");
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.InfoFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e.Message);
            });
        }
    }
}