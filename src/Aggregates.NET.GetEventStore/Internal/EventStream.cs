using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI.Exceptions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{

    internal class EventStream<T> : IEventStream where T : class, IEventSource
    {
        private const string CommitHeader = "CommitId";
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventStream<>));

        public string Bucket { get; }

        public string StreamId { get; }

        public int StreamVersion => CommitVersion + _uncommitted.Count;
        public int CommitVersion => (LastSnapshot ?? 0) + Committed.Count() - 1;

        public object CurrentMemento => _snapshot?.Payload;
        public int? LastSnapshot => _snapshot?.Version;

        public IEnumerable<IWritableEvent> Committed { get; private set; }

        public IEnumerable<IWritableEvent> Uncommitted => _uncommitted;

        public IEnumerable<IWritableEvent> OobUncommitted => _outofband;
        public IEnumerable<ISnapshot> SnapshotsUncommitted => _pendingShots;

        public int TotalUncommitted => Uncommitted.Count() + OobUncommitted.Count() + SnapshotsUncommitted.Count();

        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapshots;
        private readonly IOobHandler _oobHandler;
        private readonly IBuilder _builder;
        private readonly ISnapshot _snapshot;
        private IList<IWritableEvent> _uncommitted;
        private readonly IList<IWritableEvent> _outofband;
        private readonly IList<ISnapshot> _pendingShots;

        public EventStream(IBuilder builder, IStoreEvents store, string bucket, string streamId, IEnumerable<IWritableEvent> events, ISnapshot snapshot)
        {
            _store = store;
            _snapshots = builder?.Build<IStoreSnapshots>();
            _oobHandler = builder?.Build<IOobHandler>();
            _builder = builder;
            Bucket = bucket;
            StreamId = streamId;
            Committed = events?.ToList() ?? new List<IWritableEvent>();
            _snapshot = snapshot;

            _uncommitted = new List<IWritableEvent>();
            _outofband = new List<IWritableEvent>();
            _pendingShots = new List<ISnapshot>();

        }

        // Special constructor for building from a cached instance
        internal EventStream(IEventStream clone, IBuilder builder, IStoreEvents store, ISnapshot snapshot)
        {
            _store = store;
            _snapshots = builder.Build<IStoreSnapshots>();
            _oobHandler = builder.Build<IOobHandler>();
            _builder = builder;
            Bucket = clone.Bucket;
            StreamId = clone.StreamId;
            _snapshot = snapshot;
            Committed = clone.Committed.ToList();
            _uncommitted = new List<IWritableEvent>();
            _outofband = new List<IWritableEvent>();
            _pendingShots = new List<ISnapshot>();

            if (_snapshot != null && Committed.Any() && Committed.First().Descriptor.Version <= _snapshot.Version)
            {
                Committed = Committed.Where(x => x.Descriptor.Version > _snapshot.Version);
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
            Committed = Committed.Concat(events);
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

            IWritableEvent writable = new WritableEvent
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
                writable.Event = mutate.MutateOutgoing(writable.Event);
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
                Version = StreamVersion,
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

            if (_outofband.Any())
            {
                if (_oobHandler == null)
                    Logger.Write(LogLevel.Warn, () => $"OOB events were used on stream [{StreamId}] but no publishers have been defined!");
                else
                {
                    Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] publishing {_outofband.Count} out of band events to {_oobHandler.GetType().Name}");
                    await _oobHandler.Publish<T>(Bucket, StreamId, _outofband, commitHeaders).ConfigureAwait(false);

                }
                _outofband.Clear();
            }

            var wip = _uncommitted.ToList();
            try
            {
                if (wip.Any())
                {
                    // If we increment commit id instead of depending on a commit header, ES will do the concurrency check for us
                    foreach (var uncommitted in wip.Where(x => !x.EventId.HasValue))
                    {
                        uncommitted.EventId = startingEventId;
                        startingEventId = startingEventId.Increment();
                    }

                    // Do a quick check if any event in the current stream has the same commit id indicating the effects of this command have already been recorded
                    // Note: if the stream has snapshots we wont be checking ALL previous events - but this is a good spot check
                    //var oldCommits = this._committed.Select(x =>
                    //{
                    //    String temp;
                    //    if (!x.Descriptor.Headers.TryGetValue(CommitHeader, out temp))
                    //        return Guid.Empty;
                    //    return Guid.Parse(temp);
                    //});
                    //if (oldCommits.Any(x => x == commitId))
                    //    throw new DuplicateCommitException($"Probable duplicate message handled - discarding commit id {commitId}");

                    Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] committing {wip.Count} events");
                    await _store.WriteEvents<T>(Bucket, StreamId, CommitVersion, wip, commitHeaders).ConfigureAwait(false);
                    _uncommitted = wip;
                }
                if (_pendingShots.Any())
                {
                    Logger.Write(LogLevel.Debug, () => $"Event stream [{StreamId}] in bucket [{Bucket}] committing {_pendingShots.Count} snapshots");
                    await _snapshots.WriteSnapshots<T>(Bucket, StreamId, _pendingShots, commitHeaders).ConfigureAwait(false);
                }
                Flush(true);
            }
            catch (WrongExpectedVersionException e)
            {
                throw new VersionException(e.Message, e);
            }
            catch (CannotEstablishConnectionException e)
            {
                throw new PersistenceException(e.Message, e);
            }
            catch (OperationTimedOutException e)
            {
                throw new PersistenceException(e.Message, e);
            }
            catch (EventStoreConnectionException e)
            {
                throw new PersistenceException(e.Message, e);
            }
            return startingEventId;
        }

        public void Flush(bool committed)
        {
            if (committed)
                Committed = Committed.Concat(_uncommitted);

            _uncommitted.Clear();
            _pendingShots.Clear();
            _outofband.Clear();
        }

    }
}