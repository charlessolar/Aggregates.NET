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
        private readonly IMessageSession _endpoint;
        private readonly JsonSerializerSettings _jsonSettings;

        public Boolean ProcessingLive { get; set; }
        public Action<String, Exception> Dropped { get; set; }

        public DomainSubscriber(IBuilder builder, IEventStoreConnection client, IStreamCache cache, IMessageSession endpoint, ReadOnlySettings settings, IMessageMapper mapper)
        {
            _builder = builder;
            _client = client;
            _cache = cache;
            _settings = settings;
            _endpoint = endpoint;
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
            Logger.Write(LogLevel.Info, () => $"Endpoint '{endpoint}' subscribing to all events from END");

            var settings = new CatchUpSubscriptionSettings(readSize * readSize, readSize, false, false);
            _client.SubscribeToAllFrom(Position.End, settings, (subscription, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;
                
                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);

                if (descriptor == null) return;

                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);
                if (!(data is IEvent) || !_cache.Update(e.OriginalStreamId, new Internal.WritableEvent { Descriptor = descriptor, Event = data as IEvent, EventId = e.Event.EventId }))
                    _cache.Evict(e.OriginalStreamId);

                // Check if the event was written by this domain handler
                // We don't need to publish events saved by other domain instances
                String instance = null;
                Guid domain = Guid.Empty;
                if (descriptor.Headers == null || !descriptor.Headers.TryGetValue(Defaults.InstanceHeader, out instance) || !Guid.TryParse(instance, out domain) || domain != Defaults.Instance)
                    return;
                // Data is null for certain irrelevant eventstore messages (and we don't need to store position)
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
                Logger.Write(LogLevel.Warn,  () => $"Subscription dropped for reason: {reason}.  Exception: {e}");
                ProcessingLive = false;
                if (Dropped != null)
                    Dropped.Invoke(reason.ToString(), e);
            });
        }
    }
}
