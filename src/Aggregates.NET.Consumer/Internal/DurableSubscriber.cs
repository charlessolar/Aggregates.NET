using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class DurableSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(DurableSubscriber));
        private readonly IBuilder _builder;
        private readonly IEventStoreConnection _client;
        private readonly IPersistCheckpoints _store;
        private readonly IDispatcher _dispatcher;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;
        private Meter _fullMeter = Metric.Meter("Queue Full Exceptions", Unit.Errors);

        public DurableSubscriber(IBuilder builder, IEventStoreConnection client, IPersistCheckpoints store, IDispatcher dispatcher, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
        {
            _builder = builder;
            _client = client;
            _store = store;
            _dispatcher = dispatcher;
            _settings = settings;
            _jsonSettings = jsonSettings;
        }

        public void SubscribeToAll(String endpoint)
        {
            var saved = _store.Load(endpoint);

            var readSize = _settings.Get<Int32>("ReadSize");
            Logger.InfoFormat("Endpoint '{0}' subscribing to all events from position '{1}'", endpoint, saved);


            _client.SubscribeToAllFrom(saved, false, (subscription, e) =>
            {
                Thread.CurrentThread.Rename("Eventstore");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;

                _dispatcher.Dispatch(data, descriptor, e.OriginalPosition?.CommitPosition);

            }, liveProcessingStarted: (_) =>
            {
                Logger.Info("Live processing started");
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e);
            }, readBatchSize: readSize);
        }
    }
}