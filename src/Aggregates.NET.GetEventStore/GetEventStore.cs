using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;

namespace Aggregates
{
    public class GetEventStore : IStoreEvents
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(GetEventStore));
        private readonly IEventStoreConnection _client;
        private readonly IMessageMapper _mapper;
        private readonly JsonSerializerSettings _settings;

        public GetEventStore(IEventStoreConnection client, IMessageMapper mapper, JsonSerializerSettings settings)
        {
            _client = client;
            _mapper = mapper;
            _settings = settings;
        }

        public ISnapshot GetSnapshot<T>(String bucket, String stream) where T : class, IEntity
        {
            Logger.DebugFormat("Getting snapshot for stream '{0}' in bucket '{1}'", stream, bucket);

            var streamId = String.Format("{0}.{1}.{2}", bucket, stream, "snapshots");

            var read = _client.ReadEventAsync(streamId, StreamPosition.End, false).WaitForResult();
            if (read.Status != EventReadStatus.Success || !read.Event.HasValue)
                return null;

            var @event = read.Event.Value.Event;

            var descriptor = @event.Metadata.Deserialize(_settings);
            var data = @event.Data.Deserialize(@event.EventType, _settings);

            return new Internal.Snapshot { Version = descriptor.Version, Payload = data };
        }

        public IEventStream GetStream<T>(String bucket, String stream, Int32? start = null) where T : class, IEntity
        {
            Logger.DebugFormat("Getting stream for stream '{0}' in bucket '{1}'", stream, bucket);

            var streamId = String.Format("{0}.{1}", bucket, stream);
            var events = new List<ResolvedEvent>();

            StreamEventsSlice current;
            var sliceStart = start ?? StreamPosition.Start;
            do
            {
                current = _client.ReadStreamEventsForwardAsync(streamId, sliceStart, 200, false).WaitForResult();

                events.AddRange(current.Events);
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);

            var translatedEvents = events.Select(e =>
            {
                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _settings);

                return new Internal.WritableEvent
                {
                    Descriptor = descriptor,
                    Event = data,
                    EventId = e.Event.EventId
                };
            });

            return new Internal.EventStream<T>(this, bucket, stream, current.LastEventNumber, translatedEvents);
        }
        public void WriteSnapshots(String bucket, String stream, IEnumerable<IWritableEvent> snapshots, IDictionary<String, Object> commitHeaders)
        {
            Logger.DebugFormat("Writing {0} snapshots to stream id '{1}'", snapshots.Count(), stream);
            var streamId = String.Format("{0}.{1}.{2}", bucket, stream, "snapshots");
            
            var translatedEvents = snapshots.Select(e =>
            {
                e.Descriptor.Headers.Merge(commitHeaders);
                return new EventData(
                    e.EventId,
                    e.Event.GetType().AssemblyQualifiedName,
                    true,
                    e.Event.Serialize(_settings).AsByteArray(),
                    e.Descriptor.Serialize(_settings).AsByteArray()
                    );
            });

            _client.AppendToStreamAsync(streamId, ExpectedVersion.Any, translatedEvents).Wait();
            
        }

        public void WriteEvents(String bucket, String stream, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, Object> commitHeaders)
        {
            Logger.DebugFormat("Writing {0} events to stream id '{1}'.  Expected version: {2}", events.Count(), stream, expectedVersion);
            var streamId = String.Format("{0}.{1}", bucket, stream);

            var translatedEvents = events.Select(e =>
            {
                e.Descriptor.Headers.Merge(commitHeaders);
                return new EventData(
                    e.EventId,
                    _mapper.GetMappedTypeFor(e.Event.GetType()).AssemblyQualifiedName,
                    true,
                    e.Event.Serialize(_settings).AsByteArray(),
                    e.Descriptor.Serialize(_settings).AsByteArray()
                    );
            });

            _client.AppendToStreamAsync(streamId, expectedVersion, translatedEvents).Wait();
        }
    }
}