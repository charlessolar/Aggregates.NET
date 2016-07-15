using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Newtonsoft.Json;
using NServiceBus.Logging;
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
        private static String CommitHeader = "CommitId";
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventStream<>));
        public String Bucket { get; private set; }
        public String StreamId { get; private set; }
        public Int32 StreamVersion { get { return this._streamVersion + this._uncommitted.Count; } }
        public Int32 CommitVersion { get { return this._streamVersion; } }

        public IEnumerable<IWritableEvent> Events
        {
            get
            {
                return this._committed.Concat(this._uncommitted);
            }
        }
        public IEnumerable<IWritableEvent> Uncommitted
        {
            get
            {
                return this._uncommitted.Concat(this._outofband);
            }
        }

        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapshots;
        private readonly IBuilder _builder;
        private readonly Int32 _streamVersion;
        private IEnumerable<IWritableEvent> _committed;
        private IList<IWritableEvent> _uncommitted;
        private IList<IWritableEvent> _outofband;
        private IList<ISnapshot> _pendingShots;

        public EventStream(IBuilder builder, IStoreEvents store, IStoreSnapshots snapshots, String bucket, String streamId, Int32 streamVersion, IEnumerable<IWritableEvent> events)
        {
            this._store = store;
            this._snapshots = snapshots;
            this._builder = builder;
            this.Bucket = bucket;
            this.StreamId = streamId;
            this._streamVersion = streamVersion;
            this._committed = events.ToList();
            this._uncommitted = new List<IWritableEvent>();
            this._outofband = new List<IWritableEvent>();
            this._pendingShots = new List<ISnapshot>();
            
        }

        // Special constructor for building from a cached instance
        internal EventStream(IEventStream clone, IBuilder builder, IStoreEvents store, IStoreSnapshots snapshots)
        {
            this._store = store;
            this._snapshots = snapshots;
            this._builder = builder;
            this.Bucket = clone.Bucket;
            this.StreamId = clone.StreamId;
            this._streamVersion = clone.StreamVersion;
            this._committed = clone.Events.ToList();
            this._uncommitted = new List<IWritableEvent>();
            this._outofband = new List<IWritableEvent>();
            this._pendingShots = new List<ISnapshot>();
            
        }

        public IEventStream Clone()
        {
            return new EventStream<T>(null, null, null, Bucket, StreamId, _streamVersion, _committed);
        }

        private IWritableEvent makeWritableEvent(Object @event, IDictionary<String, String> headers, Boolean version = true)
        {

            IWritableEvent writable = new WritableEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).AssemblyQualifiedName,
                    Timestamp = DateTime.UtcNow,
                    Version = version ? this.StreamVersion + 1 : this.StreamVersion,
                    Headers = headers
                },
                Event = @event,
                EventId = Guid.NewGuid()
            };

            var mutators = _builder.BuildAll<IEventMutator>();
            if (mutators != null && mutators.Any())
                foreach (var mutate in mutators)
                {
                    Logger.DebugFormat("Mutating outgoing event {0} with mutator {1}", @event.GetType().FullName, mutate.GetType().FullName);
                    writable = mutate.MutateOutgoing(writable);
                }
            return writable;
        }

        public void AddOutOfBand(Object @event, IDictionary<String, String> headers)
        {
            _outofband.Add(makeWritableEvent(@event, headers, false));
        }

        public void Add(Object @event, IDictionary<String, String> headers)
        {
            _uncommitted.Add(makeWritableEvent(@event, headers));
        }

        public void AddSnapshot(Object memento, IDictionary<String, String> headers)
        {
            this._pendingShots.Add(new Snapshot
            {
                Bucket = this.Bucket,
                Stream = this.StreamId,
                Payload = memento,
                Version = this.StreamVersion,
                EntityType = memento.GetType().AssemblyQualifiedName,
                Timestamp = DateTime.UtcNow,
            });
        }

        public async Task Commit(Guid commitId, IDictionary<String, String> commitHeaders)
        {
            Logger.DebugFormat("Event stream {0} commiting events", this.StreamId);


            if (commitHeaders == null)
                commitHeaders = new Dictionary<String, String>();

            commitHeaders[CommitHeader] = commitId.ToString();


            try
            {
                if (_uncommitted.Any())
                {
                    // Do a quick check if any event in the current stream has the same commit id indicating the effects of this command have already been recorded
                    var oldCommits = this._committed.Select(x =>
                    {
                        String temp;
                        if (!x.Descriptor.Headers.TryGetValue(CommitHeader, out temp))
                            return Guid.Empty;
                        return Guid.Parse(temp);
                    });
                    if (oldCommits.Any(x => x == commitId))
                        throw new DuplicateCommitException($"Probable duplicate message handled - discarding commit id {commitId}");

                    Logger.DebugFormat("Event stream {0} committing {1} events", this.StreamId, _uncommitted.Count);
                    await _store.WriteEvents(this.Bucket, this.StreamId, this._streamVersion, _uncommitted, commitHeaders);
                    this._uncommitted.Clear();
                }
                if (_pendingShots.Any())
                {
                    Logger.DebugFormat("Event stream {0} committing {1} snapshots", this.StreamId, _pendingShots.Count);
                    await _snapshots.WriteSnapshots(this.Bucket, this.StreamId, _pendingShots, commitHeaders);
                    this._pendingShots.Clear();
                }
                if (_outofband.Any())
                {
                    Logger.DebugFormat("Event stream {0} committing {1} out of band events", this.StreamId, _pendingShots.Count);
                    await _store.AppendEvents(this.Bucket + ".OOB", this.StreamId, _outofband, commitHeaders);
                    this._outofband.Clear();
                }
            }
            catch (WrongExpectedVersionException e)
            {
                throw new VersionException($"Expected version {_streamVersion}", e);
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
        }


    }
}