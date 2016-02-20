using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using EventStore.ClientAPI;
using Newtonsoft.Json;

namespace Aggregates.Extensions
{
    public static class StoreExtensions
    {

        public static String Serialize(this ISnapshot snapshot, JsonSerializerSettings settings)
        {
            return JsonConvert.SerializeObject(snapshot, settings);
        }

        public static void DispatchEvents(this IEventStoreConnection client, IDispatcher dispatcher, JsonSerializerSettings settings)
        {
            // Need message mapper


            // Idea is to subscribe to stream updates from event store in order to publish the events via NSB
            // Servers not needing a persistent stream can subscribe to these events (things like email notifications)
            client.SubscribeToAllFrom(Position.End, false, (s, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(settings);

                if (descriptor == null) return;

                // Check if the event was written by this domain handler
                // We don't need to publish events saved by other domain instances
                Object header = null;
                Guid domain = Guid.Empty;
                if (!descriptor.Headers.TryGetValue(UnitOfWork.DomainHeader, out header) || !Guid.TryParse(header.ToString(), out domain) || domain != Domain.Current)
                    return;

                var data = e.Event.Data.Deserialize(e.Event.EventType, settings);
                
                if (data == null) return;

                var @event = new Internal.WritableEvent
                {
                    Descriptor = descriptor,
                    Event = data,
                    EventId = e.Event.EventId
                };

                dispatcher.Dispatch(@event);
            });
        }
    }
}