using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{

    class EventStream<T> : IEventStream where T : class, IEventSource
    {
        private static readonly ILog Logger = LogManager.GetLogger("EventStream");

        public string Bucket { get; }
        public Id StreamId { get; }
        public IEnumerable<Id> Parents { get; }

        public IEnumerable<OobDefinition> Oobs => _oobs;

        public long StreamVersion => CommitVersion + Uncommitted.Count();

        public long CommitVersion => (Snapshot?.Version ?? 0L) + Committed.Count() - 1L;
        
        public ISnapshot Snapshot => _snapshot;

        public IEnumerable<IFullEvent> Committed => _committed;

        public IEnumerable<IFullEvent> Uncommitted => _uncommitted;
        public IEnumerable<OobDefinition> PendingOobs => _newOobs;
        public IMemento PendingSnapshot => _pendingShot;


        // Don't count OOB events as Dirty
        public bool Dirty => Uncommitted.Any() || _pendingShot != null;
        public int TotalUncommitted => Uncommitted.Count() + (_pendingShot != null ? 1 : 0);
        
        private readonly ISnapshot _snapshot;

        private readonly IEnumerable<IFullEvent> _committed;
        private readonly IEnumerable<OobDefinition> _oobs;

        private readonly IList<OobDefinition> _newOobs;
        private readonly IList<IFullEvent> _uncommitted;

        private IMemento _pendingShot;
        
        public EventStream(string bucket, Id streamId, IEnumerable<Id> parents, IEnumerable<OobDefinition> oobs, IEnumerable<IFullEvent> committed, ISnapshot snapshot = null)
        {
            Bucket = bucket;
            StreamId = streamId;
            Parents = parents?.ToArray() ?? new Id[] {};
            _oobs = oobs?.ToArray() ?? new OobDefinition[] {};
            _committed = committed ?? new IFullEvent[] {};
            _snapshot = snapshot;

            _uncommitted = new List<IFullEvent>();
            _newOobs = new List<OobDefinition>();
            _pendingShot = null;
        }

        // Special constructor for building from a cached instance
        internal EventStream(IImmutableEventStream clone)
        {
            Bucket = clone.Bucket;
            StreamId = clone.StreamId;
            Parents = clone.Parents;
            _committed = clone.Committed;
            _oobs = clone.Oobs;
            _snapshot = clone.Snapshot;

            _uncommitted = new List<IFullEvent>();
            _newOobs = new List<OobDefinition>();
            _pendingShot = null;
        }

        /// <summary>
        /// Clones the stream for caching, add an event to the new clone stream optionally
        /// </summary>
        /// <param name="event"></param>
        /// <returns></returns>
        public IEventStream Clone()
        {
            return new EventStream<T>(Bucket, StreamId, Parents, _oobs, _committed, _snapshot);
        }
        
        private IFullEvent MakeWritableEvent(string streamType, IEvent @event, IDictionary<string, string> headers)
        {
            var writable = new WritableEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).AssemblyQualifiedName,
                    StreamType = streamType,
                    Bucket = Bucket,
                    StreamId = StreamId,
                    Timestamp = DateTime.UtcNow,
                    Version = StreamVersion + 1,
                    Headers = headers ?? new Dictionary<string,string>()
                },
                Event = @event
            };

            return writable;
        }
        
        public void Add(IEvent @event, IDictionary<string,string> metadata)
        {
            _uncommitted.Add(MakeWritableEvent(StreamTypes.Domain, @event, metadata));
        }

        public void AddOob(IEvent @event, string id, IDictionary<string, string> metadata)
        {
            metadata = metadata ?? new Dictionary<string, string>();
            metadata[Defaults.OobHeaderKey] = id;

            if (_oobs.All(x => x.Id != id) && _newOobs.All(x => x.Id != id))
                throw new InvalidOperationException(
                    "Can not add an oob event without defining the oob stream using DefineOob");

            _uncommitted.Add(MakeWritableEvent(StreamTypes.OOB, @event, metadata));
        }

        public void DefineOob(string id, bool transient = false, int? daysToLive = null)
        {
            if (_oobs.Any(x => x.Id == id) || _newOobs.Any(x => x.Id == id))
                return;

            Logger.Debug($"Defining new OOB on stream {StreamId} bucket {Bucket}");
            _newOobs.Add(new OobDefinition
            {
                Id = id,
                Transient = transient,
                DaysToLive = daysToLive
            });
        }

        public void AddSnapshot(IMemento memento)
        {
            _pendingShot = memento;
        }
    }
}