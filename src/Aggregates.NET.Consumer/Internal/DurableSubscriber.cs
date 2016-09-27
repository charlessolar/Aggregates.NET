using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
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
        private readonly ReadOnlySettings _settings;
        private readonly IEndpointInstance _endpoint;
        private readonly JsonSerializerSettings _jsonSettings;

        public Boolean ProcessingLive { get; set; }
        public Action<String, Exception> Dropped { get; set; }

        public DurableSubscriber(IBuilder builder, IEventStoreConnection client, IPersistCheckpoints store, IEndpointInstance endpoint, ReadOnlySettings settings, IMessageMapper mapper)
        {
            _builder = builder;
            _client = client;
            _store = store;
            _endpoint = endpoint;
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
            var saved = _store.Load(endpoint).Result;

            var readSize = _settings.Get<Int32>("ReadSize");
            Logger.Write(LogLevel.Info, () => $"Endpoint '{endpoint}' subscribing to all events from position '{saved}'");

            var settings = new CatchUpSubscriptionSettings(readSize * readSize, readSize, false, false);
            _client.SubscribeToAllFrom(saved, settings, (subscription, e) =>
            {
                Logger.Write(LogLevel.Debug, () => $"Event appeared position {e.OriginalPosition?.CommitPosition}" );
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);
                
                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;

                var options = new SendOptions();

                options.RouteToThisInstance();
                options.SetHeader("CommitPosition", e.OriginalPosition?.CommitPosition.ToString());
                options.SetHeader("EntityType", descriptor.EntityType);
                options.SetHeader("Version", descriptor.Version.ToString());
                options.SetHeader("Timestamp", descriptor.Timestamp.ToString());
                foreach (var header in descriptor.Headers)
                    options.SetHeader(header.Key, header.Value);

                try
                {
                    _endpoint.Send(data, options);
                }
                catch (SubscriptionCanceled)
                {
                    subscription.Stop();
                    throw;
                }

            }, liveProcessingStarted: (_) =>
            {
                Logger.Write(LogLevel.Info, "Live processing started");
                ProcessingLive = true;
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.Write(LogLevel.Warn, () => $"Subscription dropped for reason: {reason}.  Exception: {e}");
                ProcessingLive = false;
                if (Dropped != null)
                    Dropped.Invoke(reason.ToString(), e);
            });
        }
    }
}