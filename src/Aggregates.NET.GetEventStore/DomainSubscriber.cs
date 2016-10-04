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
using Aggregates.Internal;
using System.Threading;
using Aggregates.Exceptions;
using NServiceBus.Settings;
using Aggregates.Contracts;
using NServiceBus.MessageInterfaces;
using NServiceBus;

namespace Aggregates
{
    public class DomainSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(DomainSubscriber));
        private readonly IBuilder _builder;
        private readonly IEventStoreConnection _client;
        private readonly IStreamCache _cache;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;

        public Boolean ProcessingLive { get; set; }
        public Action<String, Exception> Dropped { get; set; }

        public DomainSubscriber(IBuilder builder, IEventStoreConnection client, IStreamCache cache, ReadOnlySettings settings, IMessageMapper mapper)
        {
            _builder = builder;
            _client = client;
            _cache = cache;
            _settings = settings;
            _jsonSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };
        }

        public void SubscribeToAll(IMessageSession bus, String endpoint)
        {
            var readSize = _settings.Get<Int32>("ReadSize");
            var compress = _settings.Get<Boolean>("Compress");
            Logger.Write(LogLevel.Info, () => $"Endpoint '{endpoint}' subscribing to all events from END");
            
            var settings = new CatchUpSubscriptionSettings(readSize * readSize, readSize, false, false);
            _client.SubscribeToAllFrom(Position.End, settings, (subscription, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                //if (!e.Event.IsJson) return;
                var metadata = e.Event.Metadata;

                // Todo: dont depend on setting, detect event compression somehow (metadata?)
                if (compress)
                    metadata = metadata.Decompress();

                var descriptor = metadata.Deserialize(_jsonSettings);

                if (descriptor == null) return;

                var data = e.Event.Data;

                if (compress)
                    data = data.Decompress();
                
                // Check if the event was written by this domain handler
                // We don't need to publish events saved by other domain instances
                String instance = null;
                Guid domain = Guid.Empty;
                if (descriptor.Headers == null || !descriptor.Headers.TryGetValue(Defaults.InstanceHeader, out instance) || !Guid.TryParse(instance, out domain) || domain != Defaults.Instance)
                    return;

                var @event = data.Deserialize(e.Event.EventType, _jsonSettings) as IEvent;
                // If a snapshot, poco, or irrelevent ES message, don't publish
                if (@event == null) return;

                var options = new PublishOptions();

                //options.RouteToThisInstance();
                // Recent Change!
                // Publish the event we read - its from the store so we know its committed, no DTC
                // Eliminates the need for consumers to "load balance" themselves using the CompetingSubscriber
                options.SetHeader("CommitPosition", e.OriginalPosition?.CommitPosition.ToString());
                options.SetHeader("EntityType", descriptor.EntityType);
                options.SetHeader("Version", descriptor.Version.ToString());
                options.SetHeader("Timestamp", descriptor.Timestamp.ToString());
                foreach (var header in descriptor.Headers)
                    options.SetHeader(header.Key, header.Value);
                
                try
                {
                    bus.Publish(@event, options);
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
                Logger.Write(LogLevel.Warn,  () => $"Subscription dropped for reason: {reason}.  Exception: {e}");
                ProcessingLive = false;
                if (Dropped != null)
                    Dropped.Invoke(reason.ToString(), e);
            });
        }
    }
}
