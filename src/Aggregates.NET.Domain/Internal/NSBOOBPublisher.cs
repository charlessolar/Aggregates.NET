using Aggregates.Contracts;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class NSBOOBPublisher : IOOBPublisher
    {
        private readonly IBus _bus;

        public NSBOOBPublisher(IBus bus)
        {
            _bus = bus;
        }


        public Task Publish<T>(String Bucket, String StreamId, IEnumerable<IWritableEvent> Events, IDictionary<String, String> commitHeaders) where T : class, IEventSource
        {
            foreach (var header in commitHeaders)
            {
                if (header.Key == Headers.OriginatingHostId)
                {
                    //is added by bus in v5
                    continue;
                }

                _bus.OutgoingHeaders[header.Key] = header.Value != null ? header.Value.ToString() : null;
            }

            foreach (var @event in Events)
            {
                _bus.SetMessageHeader(@event.Event, "EventId", @event.EventId.ToString());
                _bus.SetMessageHeader(@event.Event, "EntityType", @event.Descriptor.EntityType);
                _bus.SetMessageHeader(@event.Event, "Timestamp", @event.Descriptor.Timestamp.ToString());
                _bus.SetMessageHeader(@event.Event, "Version", @event.Descriptor.Version.ToString());


                foreach (var header in @event.Descriptor.Headers)
                {
                    _bus.SetMessageHeader(@event, header.Key, header.Value);
                }

                _bus.Publish(@event);
            }

            return Task.FromResult(0);
        }
    }
}
