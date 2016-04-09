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
                return this._uncommitted;
            }
        }

        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapshots;
        private readonly IBuilder _builder;
        private readonly Int32 _streamVersion;
        private Int32 _version;
        private IEnumerable<IWritableEvent> _committed;
        private IList<IWritableEvent> _uncommitted;
        private IList<ISnapshot> _pendingShots;
        private IDictionary<String, IEventStream> _children;

        public EventStream(IBuilder builder, IStoreEvents store, IStoreSnapshots snapshots, String bucket, String streamId, Int32 streamVersion, IEnumerable<IWritableEvent> events)
        {
            this._store = store;
            this._snapshots = snapshots;
            this._builder = builder;
            this.Bucket = bucket;
            this.StreamId = streamId;
            this._streamVersion = streamVersion;
            this._version = streamVersion;
            this._committed = events.ToList();
            this._uncommitted = new List<IWritableEvent>();
            this._pendingShots = new List<ISnapshot>();
            this._children = new Dictionary<String, IEventStream>();

            if (events == null || events.Count() == 0) return;
        }

        public IEventStream Clone()
        {
            return new EventStream<T>(_builder, _store, _snapshots, Bucket, StreamId, _streamVersion, _committed);
        }

        public void Add(Object @event, IDictionary<String, String> headers)
        {
            IWritableEvent writable = new WritableEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).AssemblyQualifiedName,
                    Timestamp = DateTime.UtcNow,
                    Version = ++this._version,
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

            _uncommitted.Add(writable);
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

            Parallel.ForEach(this._children.Values, async (child) =>
            {
                Logger.DebugFormat("Event stream {0} commiting changes to child stream {1}", this.StreamId, child.StreamId);
                await child.Commit(commitId, commitHeaders);
            });
            

            if (this._uncommitted.Count == 0)
            {
                ClearChanges();
                return;
            }

            if (commitHeaders == null)
                commitHeaders = new Dictionary<String, String>();

            commitHeaders[CommitHeader] = commitId.ToString();

            var oldCommits = Events.Select(x =>
            {
                String temp;
                if (!x.Descriptor.Headers.TryGetValue(CommitHeader, out temp))
                    return Guid.Empty;
                return Guid.Parse(temp);
            });
            if (oldCommits.Any(x => x == commitId))
                throw new DuplicateCommitException($"Probable duplicate message handled - discarding commit id {commitId}");

            Logger.DebugFormat("Event stream {0} committing {1} events", this.StreamId, _uncommitted.Count);
            try
            {
                await _store.WriteEvents(this.Bucket, this.StreamId, this._streamVersion, _uncommitted, commitHeaders);

                await _snapshots.WriteSnapshots(this.Bucket, this.StreamId, _pendingShots, commitHeaders);

                ClearChanges();
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

        public void AddChild(IEventStream stream)
        {
            Logger.DebugFormat("Event stream {0} adding child {1}", this.StreamId, stream.StreamId);
            this._children[stream.StreamId] = stream;
        }

        public void ClearChanges()
        {
            Logger.DebugFormat("Event stream {0} clearing changes", this.StreamId);
            this._uncommitted.Clear();
            this._pendingShots.Clear();
            this._children.Clear();
        }
    }
}