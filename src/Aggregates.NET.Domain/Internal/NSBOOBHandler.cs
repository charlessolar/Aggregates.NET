using Aggregates.Contracts;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class NSBOOBHandler : IOOBHandler
    {
        private readonly IMessageSession _endpoint;

        public NSBOOBHandler(IMessageSession endpoint)
        {
            _endpoint = endpoint;
        }


        public Task Publish<T>(String Bucket, String StreamId, IEnumerable<IWritableEvent> Events, IDictionary<String, String> commitHeaders) where T : class, IEventSource
        {
            var options = new PublishOptions();

            foreach (var header in commitHeaders)
            {
                if (header.Key == Headers.OriginatingHostId)
                {
                    //is added by bus in v5
                    continue;
                }
                options.SetHeader(header.Key, header.Value?.ToString());
            }

            foreach (var @event in Events)
            {
                options.SetHeader("EventId", @event.EventId.ToString());
                options.SetHeader("EntityType", @event.Descriptor.EntityType);
                options.SetHeader("Timestamp", @event.Descriptor.Timestamp.ToString());
                options.SetHeader("Version", @event.Descriptor.Version.ToString());


                foreach (var header in @event.Descriptor.Headers)
                {
                    options.SetHeader(header.Key, header.Value);
                }

                _endpoint.Publish(@event, options);
            }

            return Task.FromResult(0);
        }
        public Task<IEnumerable<IWritableEvent>> Retrieve<T>(String Bucket, String StreamId, Int32? Skip = null, Int32? Take = null, Boolean Ascending = true) where T : class, IEventSource
        {
            throw new NotImplementedException("NSB OOB handler does not support retrieving OOB events");
        }
    }
}
