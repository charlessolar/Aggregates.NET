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

        public async Task Publish<T>(string bucket, Id streamId, IEnumerable<Id> parents, IEnumerable<IFullEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {

            var parentStr = parents.BuildParentsString();
            await events.WhenAllAsync(@event =>
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

                options.SetHeader("EventId", @event.EventId.ToString());
                options.SetHeader("EntityType", @event.Descriptor.EntityType);
                options.SetHeader("Timestamp", @event.Descriptor.Timestamp.ToString("s"));
                options.SetHeader("Version", @event.Descriptor.Version.ToString());

                options.SetHeader("Bucket", bucket);
                options.SetHeader("StreamId", streamId);
                options.SetHeader("Parents", parentStr);

                foreach (var header in @event.Descriptor.Headers)
                    options.SetHeader(header.Key, header.Value);


                return Bus.Instance.Publish(@event.Event, options);
            }).ConfigureAwait(false);

        }
        public Task<IEnumerable<IFullEvent>> Retrieve<T>(string bucket, Id streamId, IEnumerable<Id> parents, long? skip = null, int? take = null, bool ascending = true) where T : class, IEventSource
        {
            throw new InvalidOperationException("NSB OOB handler does not support retrieving OOB events");
        }
        public Task<long> Size<T>(string bucket, Id streamId, IEnumerable<Id> parents) where T : class, IEventSource
        {
            // NSB oob events can't be retreived so size is naturally 0
            return Task.FromResult(0L);
        }
    }
}
