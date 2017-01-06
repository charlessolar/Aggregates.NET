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

        public int StreamVersion => CommitVersion + Uncommitted.Count();
        public int CommitVersion => (LastSnapshot ?? 0) + Committed.Count() - 1;

        public object CurrentMemento => _snapshot?.Payload;
        public int? LastSnapshot => _snapshot?.Version;

        public IEnumerable<IWritableEvent> Committed => _committed;

        public IEnumerable<IWritableEvent> Uncommitted => _uncommitted;

        public IEnumerable<IWritableEvent> OobUncommitted => _outofband;
        public IEnumerable<ISnapshot> SnapshotsUncommitted => _pendingShots;

        public bool Dirty => Uncommitted.Any() || OobUncommitted.Any() || SnapshotsUncommitted.Any();
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
        private readonly Guid _commitId;

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
            _pendingShots = new List<ISnapshot>();

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

        public Task VerifyVersion(Guid commitId)
        {
            Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] for type {typeof(T).FullName} verifying stream version {CommitVersion}");

            return _store.VerifyVersion<T>(this);
        }

        public Task Commit(Guid commitId, IDictionary<string, string> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] for type {typeof(T).FullName} commiting {_uncommitted.Count} events, {_pendingShots.Count} snapshots, {_outofband.Count} out of band");
            
            if (commitHeaders == null)
                commitHeaders = new Dictionary<string, string>();

            commitHeaders[CommitHeader] = commitId.ToString();

            var tasks = new Task[]
            {
                Task.Run(() =>
                {
                    if (!_uncommitted.Any()) return Task.CompletedTask;

                    Logger.Write(LogLevel.Debug,
                        () => $"Event stream [{StreamId}] in bucket [{Bucket}] committing {Uncommitted.Count()} events");
                    return _store.WriteStream<T>(this, commitHeaders);
                }),
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
                    if (!_pendingShots.Any()) return Task.CompletedTask;

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Event stream [{StreamId}] in bucket [{Bucket}] committing {_pendingShots.Count} snapshots");
                    return _snapshots.WriteSnapshots<T>(Bucket, StreamId, _pendingShots, commitHeaders);
                })
            };
            return Task.WhenAll(tasks);
        }

        public void Flush(bool committed)
        {
            if (committed)
                _committed = _committed.Concat(_uncommitted).ToList();

            _uncommitted.Clear();
            _pendingShots.Clear();
            _outofband.Clear();
        }

    }
}