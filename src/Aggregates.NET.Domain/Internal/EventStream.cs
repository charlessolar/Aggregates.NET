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

        public string StreamType { get; }
        public string Bucket { get; }

        public string StreamId { get; }

        public int StreamVersion => CommitVersion + Uncommitted.Count();
        // +1 because Version is 0 indexed.  If we have a stream of 100 events with a snapshot at event 100 the snapshot version would be 100
        // When we read the stream we'll get 1 snapshot and 0 events making CommitVersion 99 if no +1
        // -1 because if you have a stream with 1 event CommitVersion should be 0
        public int CommitVersion => (Snapshot?.Version + 1 ?? 0) + Committed.Count() - 1;

        public object CurrentMemento => _snapshot?.Payload;
        public ISnapshot Snapshot => _snapshot;

        public IEnumerable<IWritableEvent> Committed => _committed;

        public IEnumerable<IWritableEvent> Uncommitted => _uncommitted;

        public IEnumerable<IWritableEvent> OobUncommitted => _outofband;

        public bool Dirty => Uncommitted.Any() || OobUncommitted.Any() || _pendingShot != null;
        public int TotalUncommitted => Uncommitted.Count() + OobUncommitted.Count() + (_pendingShot != null ? 1 : 0);

        private readonly Guid _commitId;
        private readonly IStoreStreams _store;
        private readonly IStoreSnapshots _snapshots;
        private readonly IOobHandler _oobHandler;
        private readonly IBuilder _builder;
        private readonly ISnapshot _snapshot;
        private IEnumerable<IWritableEvent> _committed;
        private readonly IList<IWritableEvent> _uncommitted;
        private readonly IList<IWritableEvent> _outofband;
        private ISnapshot _pendingShot;

        public EventStream(IBuilder builder, IStoreStreams store, string streamType, string bucket, string streamId, IEnumerable<IWritableEvent> events, ISnapshot snapshot)
        {
            _store = store;
            _snapshots = builder?.Build<IStoreSnapshots>();
            _oobHandler = builder?.Build<IOobHandler>();
            _builder = builder;
            StreamType = streamType;
            Bucket = bucket;
            StreamId = streamId;
            _committed = events?.ToList() ?? new List<IWritableEvent>();
            _snapshot = snapshot;

            _uncommitted = new List<IWritableEvent>();
            _outofband = new List<IWritableEvent>();
            _pendingShot = null;

            // Todo: this is a hack
            // Get the commit id of the current message because we need it to make writable events
            _commitId = builder?.Build<IUnitOfWork>().CommitId ?? Guid.Empty;
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
            _pendingShot = null; ;

            // Todo: this is a hack
            // Get the commit id of the current message because we need it to make writable events
            _commitId = builder?.Build<IUnitOfWork>().CommitId ?? Guid.Empty;

            // The commit version is calculated based on an existing snapshot.
            // If restoring from cache with a new snapshot, we'll remove committed events before the snapshot
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

            return new EventStream<T>(null, null, StreamType, Bucket, StreamId, committed, _snapshot);
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
                    StreamType = StreamType,
                    Bucket = Bucket,
                    StreamId = StreamId,
                    Timestamp = DateTime.UtcNow,
                    Version = version ? StreamVersion + 1 : StreamVersion,
                    Headers = headers ?? new Dictionary<string,string>()
                },
                EventId = UnitOfWork.NextEventId(_commitId),
                Event = @event
            };

            var mutators = _builder.BuildAll<IEventMutator>();
            if (!mutators.Any()) return writable;

            IMutating mutated = new Mutating(writable.Event, writable.Descriptor.Headers);
            foreach (var mutate in mutators)
            {
                Logger.Write(LogLevel.Debug, () => $"Mutating outgoing event {@event.GetType().FullName} with mutator {mutate.GetType().FullName}");
                mutated = mutate.MutateOutgoing(mutated);
            }

            foreach (var header in mutated.Headers)
                writable.Descriptor.Headers[header.Key] = header.Value;
            writable.Event = mutated.Message;

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

        public void AddSnapshot(object memento)
        {
            var snapshot = new Snapshot
            {
                Bucket = Bucket,
                StreamId = StreamId,
                Payload = memento,
                Version = StreamVersion,
                EntityType = memento.GetType().AssemblyQualifiedName,
                Timestamp = DateTime.UtcNow,
            };
            _pendingShot = snapshot;
        }

        public Task VerifyVersion(Guid commitId)
        {
            return _store.VerifyVersion<T>(this);
        }

        public async Task Commit(Guid commitId, IDictionary<string, string> commitHeaders)
        {
            var hasSnapshot = _pendingShot == null ? "no" : "with";
            Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] for type {typeof(T).FullName} commiting {_uncommitted.Count} events, {_outofband.Count} out of band, {hasSnapshot} snapshot");

            // Flush events first, guarantee consistency through expected version THEN write snapshots and OOB
            if (_uncommitted.Any())
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Event stream [{StreamId}] in bucket [{Bucket}] committing {Uncommitted.Count()} events");
                await _store.WriteStream<T>(this, commitHeaders).ConfigureAwait(false);
            }

            var tasks = new Task[]
            {
                Task.Run(() =>
                {
                    if (!_outofband.Any()) return Task.CompletedTask;

                    if (_oobHandler == null)
                        Logger.Write(LogLevel.Warn,
                            () => $"OOB events were used on stream [{StreamId}] but no publishers have been defined!");
                    else
                    {
                        Logger.Write(LogLevel.Debug,
                            () =>
                                    $"Event stream [{StreamId}] in bucket [{Bucket}] publishing {_outofband.Count} out of band events to {_oobHandler.GetType().Name}");
                        return _oobHandler.Publish<T>(Bucket, StreamId, _outofband, commitHeaders);
                    }
                    return Task.CompletedTask;
                }),
                Task.Run(() =>
                {
                    if (_pendingShot == null) return Task.CompletedTask;

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Event stream [{StreamId}] in bucket [{Bucket}] committing snapshot");
                    return _snapshots.WriteSnapshots<T>(Bucket, StreamId, _pendingShot, commitHeaders);
                })
            };
            await Task.WhenAll(tasks).ConfigureAwait(false);
            Flush(true);
        }

        public void Flush(bool committed)
        {
            if (committed)
                _committed = _committed.Concat(_uncommitted).ToList();

            _pendingShot = null;
            _uncommitted.Clear();
            _outofband.Clear();
        }

    }
}