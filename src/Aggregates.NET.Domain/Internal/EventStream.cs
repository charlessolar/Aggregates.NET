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
        private const string CommitHeader = "CommitId";
        private static readonly ILog Logger = LogManager.GetLogger("EventStream");

        public string Bucket { get; }

        public string StreamId { get; }

        public int StreamVersion => CommitVersion + _uncommitted.Count;
        public int CommitVersion => (LastSnapshot ?? 0) + Committed.Count() - 1;

        public object CurrentMemento => _snapshot?.Payload;
        public int? LastSnapshot => _snapshot?.Version;

        public IEnumerable<IWritableEvent> Committed => _committed;

        public IEnumerable<IWritableEvent> Uncommitted => _uncommitted;

        public IEnumerable<IWritableEvent> OobUncommitted => _outofband;
        public IEnumerable<ISnapshot> SnapshotsUncommitted => _pendingShots;

        public int TotalUncommitted => Uncommitted.Count() + OobUncommitted.Count() + SnapshotsUncommitted.Count();

        private readonly IStoreStreams _store;
        private readonly IStoreSnapshots _snapshots;
        private readonly IOobHandler _oobHandler;
        private readonly IBuilder _builder;
        private readonly ISnapshot _snapshot;
        private IEnumerable<IWritableEvent> _committed;
        private readonly IList<IWritableEvent> _uncommitted;
        private readonly IList<IWritableEvent> _outofband;
        private readonly IList<ISnapshot> _pendingShots;

        public EventStream(IBuilder builder, IStoreStreams store, string bucket, string streamId, IEnumerable<IWritableEvent> events, ISnapshot snapshot)
        {
            _store = store;
            _snapshots = builder?.Build<IStoreSnapshots>();
            _oobHandler = builder?.Build<IOobHandler>();
            _builder = builder;
            Bucket = bucket;
            StreamId = streamId;
            _committed = events?.ToList() ?? new List<IWritableEvent>();
            _snapshot = snapshot;

            _uncommitted = new List<IWritableEvent>();
            _outofband = new List<IWritableEvent>();
            _pendingShots = new List<ISnapshot>();

        }

        // Special constructor for building from a cached instance
        internal EventStream(IEventStream clone, IBuilder builder, IStoreStreams store, ISnapshot snapshot)
        {
            _store = store;
            _snapshots = builder.Build<IStoreSnapshots>();
            _oobHandler = builder.Build<IOobHandler>();
            _builder = builder;
            Bucket = clone.Bucket;
            StreamId = clone.StreamId;
            _snapshot = snapshot;
            _committed = clone.Committed.ToList();
            _uncommitted = new List<IWritableEvent>();
            _outofband = new List<IWritableEvent>();
            _pendingShots = new List<ISnapshot>();

            if (_snapshot != null && Committed.Any() && Committed.First().Descriptor.Version <= _snapshot.Version)
            {
                _committed = _committed.Where(x => x.Descriptor.Version > _snapshot.Version);
            }
        }

        /// <summary>
        /// Clones the stream for caching, add an event to the new clone stream optionally
        /// </summary>
        /// <param name="event"></param>
        /// <returns></returns>
        public IEventStream Clone(IWritableEvent @event = null)
        {
            var committed = Committed.ToList();
            if (@event != null)
                committed.Add(@event);

            return new EventStream<T>(null, null, Bucket, StreamId, committed, _snapshot);
        }

        void IEventStream.Concat(IEnumerable<IWritableEvent> events)
        {
            _committed = _committed.Concat(events);
        }

        public Task<IEnumerable<IWritableEvent>> AllEvents(bool? backwards)
        {
            return backwards == true ? _store.GetEventsBackwards<T>(Bucket, StreamId) : _store.GetEvents<T>(Bucket, StreamId);
        }
        public Task<IEnumerable<IWritableEvent>> OobEvents(bool? backwards)
        {
            return _oobHandler.Retrieve<T>(Bucket, StreamId, ascending: !(backwards ?? false));
        }

        private IWritableEvent MakeWritableEvent(IEvent @event, IDictionary<string, string> headers, bool version = true)
        {

            var writable = new WritableEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).AssemblyQualifiedName,
                    Timestamp = DateTime.UtcNow,
                    Version = version ? StreamVersion + 1 : StreamVersion,
                    Headers = headers
                },
                Event = @event
            };

            var mutators = _builder.BuildAll<IEventMutator>();
            if (mutators == null) return writable;

            foreach (var mutate in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating outgoing event {@event.GetType().FullName} with mutator {mutate.GetType().FullName}");
                writable.Event = mutate.MutateOutgoing((IEvent)writable.Event);
            }
            return writable;
        }

        public void AddOutOfBand(IEvent @event, IDictionary<string, string> headers)
        {
            _outofband.Add(MakeWritableEvent(@event, headers, false));
        }

        public void Add(IEvent @event, IDictionary<string, string> headers)
        {
            _uncommitted.Add(MakeWritableEvent(@event, headers));
        }

        public void AddSnapshot(object memento, IDictionary<string, string> headers)
        {
            var snapshot = new Snapshot
            {
                Bucket = Bucket,
                Stream = StreamId,
                Payload = memento,
                Version = StreamVersion + 1,
                EntityType = memento.GetType().AssemblyQualifiedName,
                Timestamp = DateTime.UtcNow
            };
            _pendingShots.Add(snapshot);
        }

        public async Task<Guid> Commit(Guid commitId, Guid startingEventId, IDictionary<string, string> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] for type {typeof(T).FullName} commiting {_uncommitted.Count} events, {_pendingShots.Count} snapshots, {_outofband.Count} out of band");


            if (commitHeaders == null)
                commitHeaders = new Dictionary<string, string>();

            commitHeaders[CommitHeader] = commitId.ToString();

            bool readOnly = true;

            if (Uncommitted.Any())
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Event stream [{StreamId}] in bucket [{Bucket}] committing {Uncommitted.Count()} events");
                startingEventId = await _store.WriteStream<T>(this, startingEventId, commitHeaders).ConfigureAwait(false);
                readOnly = false;
            }

            if (_outofband.Any())
            {
                if (_oobHandler == null)
                    Logger.Write(LogLevel.Warn, () => $"OOB events were used on stream [{StreamId}] but no publishers have been defined!");
                else
                {
                    Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] publishing {_outofband.Count} out of band events to {_oobHandler.GetType().Name}");
                    await _oobHandler.Publish<T>(Bucket, StreamId, _outofband, commitHeaders).ConfigureAwait(false);
                }
                readOnly = false;
                _outofband.Clear();
            }

            if (_pendingShots.Any())
            {
                Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] committing {_pendingShots.Count} snapshots");
                await _snapshots.WriteSnapshots<T>(Bucket, StreamId, _pendingShots, commitHeaders).ConfigureAwait(false);
                readOnly = false;
            }

            if(readOnly)
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Event stream [{StreamId}] in bucket [{Bucket}] has no new events - verifying version at the store is same");
                await _store.VerifyVersion<T>(this).ConfigureAwait(false);
            }
            Flush(true);
            return startingEventId;
        }

        public void Flush(bool committed)
        {
            if (committed)
                _committed = _committed.Concat(_uncommitted);

            _uncommitted.Clear();
            _pendingShots.Clear();
            _outofband.Clear();
        }

    }
}