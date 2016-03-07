using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using Aggregates.Contracts;
using System.Threading;

namespace Aggregates
{
    public class VolatileSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(VolatileSubscriber));
        private readonly IBuilder _builder;
        private readonly IEventStoreConnection _client;
        private readonly IDispatcher _dispatcher;
        private readonly JsonSerializerSettings _settings;

        public VolatileSubscriber(IBuilder builder, IEventStoreConnection client, IDispatcher dispatcher, JsonSerializerSettings settings)
        {
            _builder = builder;
            _client = client;
            _dispatcher = dispatcher;
            _settings = settings;
        }

        public void SubscribeToAll(String endpoint)
        {
            Logger.InfoFormat("Endpoint '{0}' subscribing to all events from END", endpoint);
            _client.SubscribeToAllFrom(Position.End, false, (_, e) =>
            {
                Thread.CurrentThread.Rename("Eventstore");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _settings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position)
                if (data == null) return;

                _dispatcher.Dispatch(data, descriptor, e.OriginalPosition?.CommitPosition);

            }, liveProcessingStarted: (_) =>
            {
                Logger.Info("Live processing started");
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e);
            });
        }
    }
}