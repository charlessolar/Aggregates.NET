using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus;

namespace Aggregates.Internal
{
    class NsbOobHandler : IOobHandler
    {
        private readonly IMessageSession _endpoint;

        public NsbOobHandler(IMessageSession endpoint)
        {
            _endpoint = endpoint;
        }


        public async Task Publish<T>(string bucket, Id streamId, IEnumerable<Id> parents, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var options = new PublishOptions();

            foreach (var header in commitHeaders)
            {
                if (header.Key == Headers.OriginatingHostId)
                {
                    //is added by bus in v5
                    continue;
                }
                options.SetHeader(header.Key, header.Value);
            }

            await events.WhenAllAsync(async @event =>
            {

                options.SetHeader("EventId", @event.EventId.ToString());
                options.SetHeader("EntityType", @event.Descriptor.EntityType);
                options.SetHeader("Timestamp", @event.Descriptor.Timestamp.ToString(CultureInfo.InvariantCulture));
                options.SetHeader("Version", @event.Descriptor.Version.ToString());

                options.SetHeader("Bucket", bucket);
                options.SetHeader("StreamId", streamId);
                options.SetHeader("Parents", parents.BuildParentsString());

                foreach (var header in @event.Descriptor.Headers)
                    options.SetHeader(header.Key, header.Value);
            

                await _endpoint.Publish(@event.Event, options).ConfigureAwait(false);
            }).ConfigureAwait(false);
            
        }
        public Task<IEnumerable<IWritableEvent>> Retrieve<T>(string bucket, Id streamId, IEnumerable<Id> parents, int? skip = null, int? take = null, bool ascending = true) where T : class, IEventSource
        {
            throw new NotImplementedException("NSB OOB handler does not support retrieving OOB events");
        }
    }
}
