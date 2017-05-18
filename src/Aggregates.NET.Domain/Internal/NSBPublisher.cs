using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus;

namespace Aggregates.Internal
{
    class NSBPublisher : IMessagePublisher
    {

        public async Task Publish<T>(string streamName, IEnumerable<IFullEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
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

                options.SetHeader("StreamName", streamName);

                foreach (var header in @event.Descriptor.Headers)
                    options.SetHeader(header.Key, header.Value);


                return Bus.Instance.Publish(@event.Event, options);
            }).ConfigureAwait(false);

        }
    }
}
