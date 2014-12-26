using Aggregates.Contracts;
using NEventStore;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    // inspired / taken from NEventStore.CommonDomain
    // https://github.com/NEventStore/NEventStore/blob/master/src/NEventStore/CommonDomain/Persistence/EventStore/EventStoreRepository.cs

    public class Repository<T> : IRepository<T> where T : class, IEventSourceBase
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Repository<>));
        private readonly IStoreEvents _store;
        private readonly IContainer _container;

        private readonly ConcurrentDictionary<String, ISnapshot> _snapshots = new ConcurrentDictionary<String, ISnapshot>();
        private readonly ConcurrentDictionary<String, IEventStream> _streams = new ConcurrentDictionary<String, IEventStream>();
        private bool _disposed;

        public Repository(IContainer container, IStoreEvents store)
        {
            _container = container;
            _store = store;
        }

        public void Commit(Guid commitId, IDictionary<String, String> headers)
        {
            foreach (var stream in _streams)
            {
                headers.ToList().ForEach(h => stream.Value.UncommittedHeaders[h.Key] = h.Value);
                try
                {
                    stream.Value.CommitChanges(commitId);
                }
                catch (ConcurrencyException e)
                {
                    // Send to aggregate ?
                    stream.Value.ClearChanges();
                    throw new ConflictingCommandException(e.Message, e);
                }
                catch (DuplicateCommitException)
                {
                    stream.Value.ClearChanges();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            lock (_streams)
            {
                foreach (var stream in _streams)
                {
                    stream.Value.Dispose();
                }

                _snapshots.Clear();
                _streams.Clear();
            }
            _disposed = true;
        }


        public T Get<T, TId>(TId id) where T : class, IEventSource<TId>
        {
            return Get<T, TId>(Bucket.Default, id);
        }

        public T Get<T, TId>(TId id, Int32 version) where T : class, IEventSource<TId>
        {
            return Get<T, TId>(Bucket.Default, id, version);
        }

        public T Get<T, TId>(String bucketId, TId id) where T : class, IEventSource<TId>
        {
            return Get<T, TId>(bucketId, id, Int32.MaxValue);
        }

        public T Get<T, TId>(String bucketId, TId id, Int32 version) where T : class, IEventSource<TId>
        {
            Logger.DebugFormat("Retreiving aggregate id {0} version {1} from bucket {2} in store", id, version, bucketId);

            ISnapshot snapshot = GetSnapshot(bucketId, id, version);
            IEventStream stream = OpenStream(bucketId, id, version, snapshot);

            // Use a child container to provide the root with a singleton stream and possibly some other future stuff
            using (var container = _container.BuildChildContainer())
            {
                container.Configure<IEventStream>(() => stream, global::NServiceBus.DependencyLifecycle.SingleInstance);
                var aggregate = (T)container.Build(typeof(T));

                if (snapshot != null)
                    aggregate.RestoreSnapshot(snapshot);

                if (version == 0 || aggregate.Version < version)
                {
                    // If they GET a currently open root, apply all the uncommitted events too
                    var events = stream.CommittedEvents.Concat(stream.UncommittedEvents);

                    aggregate.Hydrate(events.Take(version - aggregate.Version).Select(e => e.Body));

                }

                return aggregate;
            }
        }

        public T New<T, TId, TEvent>(TId id, Action<TEvent> action) where T : class, IEventSource<TId>
        {
            return New<T, TId, TEvent>(Bucket.Default, id, action);
        }

        public T New<T, TId, TEvent>(String bucketId, TId id, Action<TEvent> action) where T : class, IEventSource<TId>
        {
            // Use a child container to provide the root with a singleton stream and possibly some other future stuff
            using (var container = _container.BuildChildContainer())
            {
                var stream = PrepareStream(bucketId, id);
                container.Configure<IEventStream>(() => stream, global::NServiceBus.DependencyLifecycle.SingleInstance);
                var aggregate = (T)container.Build(typeof(T));

                aggregate.Apply(action);

                return aggregate;
            }
        }


        private ISnapshot GetSnapshot<TId>(String bucketId, TId id, int version)
        {
            ISnapshot snapshot;
            var snapshotId = String.Format("{0}/{1}", bucketId, id);
            if (!_snapshots.TryGetValue(snapshotId, out snapshot))
            {
                _snapshots[snapshotId] = snapshot = _store.Advanced.GetSnapshot(bucketId, id.ToString(), version);
            }

            return snapshot;
        }

        private IEventStream OpenStream<TId>(String bucketId, TId id, int version, ISnapshot snapshot)
        {
            IEventStream stream;
            var streamId = String.Format("{0}/{1}", bucketId, id);
            if (_streams.TryGetValue(streamId, out stream))
                return stream;

            if (snapshot == null)
                return _streams[streamId] = _store.OpenStream(bucketId, id.ToString(), Int32.MinValue, version);
            else
                return _streams[streamId] = _store.OpenStream(snapshot, version);
        }

        private IEventStream PrepareStream<TId>(String bucketId, TId id)
        {
            IEventStream stream;
            var streamId = String.Format("{0}/{1}", bucketId, id);
            if (!_streams.TryGetValue(streamId, out stream))
                _streams[streamId] = stream = _store.CreateStream(bucketId, id.ToString());

            return stream;
        }
    }
}
