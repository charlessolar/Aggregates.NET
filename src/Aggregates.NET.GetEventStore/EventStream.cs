using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Newtonsoft.Json;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class EventStream<T> : IEventStream where T : class, IEntity
    {
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

        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapshots;
        private readonly Int32 _streamVersion;
        private Int32 _version;
        private IEnumerable<WritableEvent> _committed;
        private IList<WritableEvent> _uncommitted;
        private IList<ISnapshot> _pendingShots;
        private IList<IEventStream> _children;

        public EventStream(IStoreEvents store, IStoreSnapshots snapshots, String bucket, String streamId, Int32 streamVersion, IEnumerable<WritableEvent> events)
        {
            this._store = store;
            this._snapshots = snapshots;
            this.Bucket = bucket;
            this.StreamId = streamId;
            this._streamVersion = streamVersion;
            this._version = streamVersion;
            this._committed = events.ToList();
            this._uncommitted = new List<WritableEvent>();
            this._pendingShots = new List<ISnapshot>();
            this._children = new List<IEventStream>();

            if (events == null || events.Count() == 0) return;
        }

        public void Add(Object @event, IDictionary<String, Object> headers)
        {
            this._uncommitted.Add(new WritableEvent
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
            });
        }

        public void AddSnapshot<TId>(TId id, Object snapshot, IDictionary<String, Object> headers)
        {
            this._pendingShots.Add(new Snapshot
            {
                Bucket = this.Bucket,
                Stream = this.StreamId,
                Payload = snapshot,
                Version = this._version,
                EntityType = snapshot.GetType().AssemblyQualifiedName,
                Timestamp = DateTime.UtcNow,
            });
        }

        public void Commit(Guid commitId, IDictionary<String, Object> commitHeaders)
        {
            foreach (var child in this._children)
                child.Commit(commitId, commitHeaders);

            if (this._uncommitted.Count == 0) return;

            if (commitHeaders == null)
                commitHeaders = new Dictionary<String, Object>();

            commitHeaders["CommitId"] = commitId;

            try
            {
                _store.WriteEvents(this.Bucket, this.StreamId, this._streamVersion, _uncommitted, commitHeaders);

                _snapshots.WriteSnapshots(this.Bucket, this.StreamId, _pendingShots);

                ClearChanges();
            }
            catch (WrongExpectedVersionException e)
            {
                // Todo: Send to aggregate for conflict resolution
                ClearChanges();
                throw new ConflictingCommandException(e.Message, e);
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
            this._children.Add(stream);
        }

        public void ClearChanges()
        {
            this._uncommitted.Clear();
            this._pendingShots.Clear();
        }
    }
}