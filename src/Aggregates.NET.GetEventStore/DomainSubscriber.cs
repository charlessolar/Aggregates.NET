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

namespace Aggregates
{
    public class DomainSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(DomainSubscriber));
        private readonly IBuilder _builder;
        private readonly IEventStoreConnection _client;
        private readonly IDispatcher _dispatcher;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;

        public Boolean ProcessingLive { get; set; }
        public Action<String, Exception> Dropped { get; set; }

        public DomainSubscriber(IBuilder builder, IEventStoreConnection client, IDispatcher dispatcher, ReadOnlySettings settings, IMessageMapper mapper)
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
            Logger.Write(LogLevel.Info, () => $"Endpoint '{endpoint}' subscribing to all events from END");

            var settings = new CatchUpSubscriptionSettings(readSize * readSize, readSize, false, false);
            _client.SubscribeToAllFrom(Position.End, settings, (subscription, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);

                if (descriptor == null) return;
                // Check if the event was written by this domain handler
                // We don't need to publish events saved by other domain instances
                String header = null;
                Guid domain = Guid.Empty;
                if (descriptor.Headers == null || !descriptor.Headers.TryGetValue(Defaults.DomainHeader, out header) || !Guid.TryParse(header, out domain) || domain != Defaults.Domain)
                    return;

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
