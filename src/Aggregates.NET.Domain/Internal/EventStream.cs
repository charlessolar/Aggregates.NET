using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Newtonsoft.Json;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class EventStream<T> : IEventStream where T : class, IEntity
    {
        internal class EventData
        {
            public Guid EventId { get; set; }
            public EventDescriptor Descriptor { get; set; }
            public Object Event { get; set; }
        }

        public String StreamId { get; private set; }
        public String BucketId { get; private set; }
        public Int32 StreamVersion { get; private set; }

        public ISnapshot Snapshot { get; private set; }
        public IEnumerable<Object> Events
        {
            get
            {
                return this._committed.Concat(this._uncommitted).Select(x => x.Event);
            }
        }

        private readonly IBuilder _builder;
        private IEnumerable<EventData> _committed;
        private ICollection<EventData> _uncommitted;

        public EventStream(IBuilder builder, String streamId, String bucketId, Int32 streamVersion, ISnapshot snapshot, IEnumerable<EventStore.ClientAPI.EventData> events)
        {
            this._builder = builder;
            this.StreamId = streamId;
            this.BucketId = bucketId;
            this.StreamVersion = streamVersion;
            this.Snapshot = snapshot;
            this._uncommitted = new List<EventData>();
            this._committed = new List<EventData>();

            if (events == null || events.Count() == 0) return;

            this._committed = events.Select(e =>
            {
                var descriptor = e.Metadata.Deserialize(_builder.Build<JsonSerializerSettings>());
                var @event = e.Data.Deserialize(descriptor.EventType, _builder.Build<JsonSerializerSettings>());

                return new EventData
                {
                    EventId = e.EventId,
                    Descriptor = descriptor,
                    Event = @event
                };
            });
        }

        public void Add(Object @event, IDictionary<String, Object> headers)
        {
            this.StreamVersion++;
            this._uncommitted.Add(new EventData
            {
                EventId = Guid.NewGuid(),
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).FullName,
                    EventType = @event.GetType().FullName,
                    Timestamp = DateTime.UtcNow,
                    Version = this.StreamVersion,
                    Headers = headers
                },
                Event = @event
            });
        }

        public void Commit(Guid commitId, IDictionary<String, Object> commitHeaders)
        {
            if (this._uncommitted.Count == 0) return;

            var eventData = this._uncommitted.Select(e =>
            {
                e.Descriptor.Headers = e.Descriptor.Headers.Merge(commitHeaders);
                var @event = e.Event.Serialize(_builder.Build<JsonSerializerSettings>());
                var descriptor = e.Descriptor.Serialize(_builder.Build<JsonSerializerSettings>());

                return new EventStore.ClientAPI.EventData(
                    e.EventId,
                    e.Descriptor.EventType,
                    true,
                    @event.AsByteArray(),
                    descriptor.AsByteArray()
                    );
            });

            var store = _builder.Build<IStoreEvents>();
            store.WriteToStream(this.StreamId, this.StreamVersion, eventData);
        }

        public void ClearChanges()
        {
            this._uncommitted.Clear();
        }
    }
}