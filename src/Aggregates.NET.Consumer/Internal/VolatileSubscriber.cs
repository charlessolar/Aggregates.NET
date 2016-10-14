using System;
using System.Globalization;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    public class VolatileSubscriber : IEventSubscriber
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(VolatileSubscriber));
        private readonly IEventStoreConnection _client;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;

        public bool ProcessingLive { get; set; }
        public Action<string, Exception> Dropped { get; set; }

        public VolatileSubscriber(IEventStoreConnection client, ReadOnlySettings settings, IMessageMapper mapper)
        {
            _client = client;
            _settings = settings;
            _jsonSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };
        }

        public void SubscribeToAll(IMessageSession bus, string endpoint)
        {
            var readSize = _settings.Get<int>("ReadSize");
            Logger.Write(LogLevel.Info, () => $"Endpoint '{endpoint}' subscribing to all events from END");
            
            var settings = new CatchUpSubscriptionSettings(readSize * readSize, readSize, false, false);
            _client.SubscribeToAllFrom(Position.End, settings, (subscription, e) =>
            {
                Logger.Write(LogLevel.Debug, () => $"Event appeared position {e.OriginalPosition?.CommitPosition}");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position)
                if (data == null) return;

                var options = new SendOptions();

                options.RouteToThisInstance();
                options.SetHeader("CommitPosition", e.OriginalPosition?.CommitPosition.ToString());
                options.SetHeader("EntityType", descriptor.EntityType);
                options.SetHeader("Version", descriptor.Version.ToString());
                options.SetHeader("Timestamp", descriptor.Timestamp.ToString(CultureInfo.InvariantCulture));
                foreach (var header in descriptor.Headers)
                    options.SetHeader(header.Key, header.Value);

                try
                {
                    bus.Send(data, options).Wait();
                }
                catch (SubscriptionCanceled)
                {
                    subscription.Stop();
                    throw;
                }

            }, liveProcessingStarted: _ =>
            {
                Logger.Write(LogLevel.Info, "Live processing started");
                ProcessingLive = true;
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.Write(LogLevel.Warn, () => $"Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
                ProcessingLive = false;
                Dropped?.Invoke(reason.ToString(), e);
            });
        }
    }
}