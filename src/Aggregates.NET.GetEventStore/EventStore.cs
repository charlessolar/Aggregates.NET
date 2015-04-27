using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class EventStore : IStoreEvents
    {
        private readonly IEventStoreConnection _client;
        private readonly JsonSerializerSettings _settings;

        public EventStore(IEventStoreConnection client, JsonSerializerSettings settings)
        {
            _client = client;
            _settings = settings;
        }

        public ISnapshot GetSnapshot<T>(String stream) where T : class, IEntity
        {
            var read = _client.ReadEventAsync(stream + ".snapshots", StreamPosition.End, false).WaitForResult();
            if (read.Status != EventReadStatus.Success)
                return null;

            var snapshot = read.Event.Value.Event.Data.Deserialize<ISnapshot>(_settings);

            return snapshot;
        }

        public IEventStream GetStream<T>(String stream, Int32? start = null) where T : class, IEntity
        {
            var events = new List<ResolvedEvent>();

            StreamEventsSlice current;
            var sliceStart = start ?? StreamPosition.Start;
            do
            {
                current = _client.ReadStreamEventsForwardAsync(stream, sliceStart, 200, false).WaitForResult();

                events.AddRange(current.Events);
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);

            var translatedEvents = events.Select(e =>
            {
                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(descriptor.EventType, _settings);

                return new Internal.WritableEvent
                {
                    Descriptor = descriptor,
                    Event = data,
                    EventId = e.Event.EventId
                };
            });

            return new Internal.EventStream<T>(this, stream, current.LastEventNumber, translatedEvents);
        }


        public void WriteToStream(String stream, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, Object> commitHeaders)
        {
            var translatedEvents = events.Select(e =>
            {
                e.Descriptor.Headers.Merge(commitHeaders);
                return new EventData(
                    e.EventId,
                    e.Descriptor.EventType.toLowerCamelCase(),
                    true,
                    e.Event.Serialize(_settings).AsByteArray(),
                    e.Descriptor.Serialize(_settings).AsByteArray()
                    );
            });

            _client.AppendToStreamAsync(stream, expectedVersion, translatedEvents).Wait();
        }
    }
}