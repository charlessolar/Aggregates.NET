using Aggregates.Contracts;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    // inspired / taken from NEventStore.CommonDomain
    // https://github.com/NEventStore/NEventStore/blob/master/src/NEventStore/CommonDomain/Persistence/EventStore/EventStoreRepository.cs

    public class Repository<T> : IRepository<T> where T : class, IAggregate
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Repository<>));
        private readonly IStoreEvents _store;
        private readonly IBuilder _builder;

        private readonly ConcurrentDictionary<String, ISnapshot> _snapshots = new ConcurrentDictionary<String, ISnapshot>();
        private readonly ConcurrentDictionary<String, IEventStream> _streams = new ConcurrentDictionary<String, IEventStream>();
        private Boolean _disposed;

        public Repository(IBuilder builder)
        {
            _builder = builder;
            _store = _builder.Build<IStoreEvents>();
        }

        void IRepository.Commit(Guid commitId, IDictionary<String, Object> headers)
        {
            foreach (var stream in _streams)
            {
                try
                {
                    stream.Value.Commit(_store, commitId, headers);
                }
                catch (WrongExpectedVersionException e)
                {
                    // Send to aggregate ?
                    stream.Value.ClearChanges();
                    throw new ConflictingCommandException(e.Message, e);
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
            if (_disposed || !disposing)
                return;

            _snapshots.Clear();
            _streams.Clear();

            _disposed = true;
        }


        public T Get<TId>(TId id)
        {
            return Get<TId>(Bucket.Default, id);
        }

        public T Get<TId>(TId id, Int32 version)
        {
            return Get<TId>(Bucket.Default, id, version);
        }

        public T Get<TId>(String bucketId, TId id)
        {
            return Get<TId>(bucketId, id, StreamPosition.End);
        }

        public T Get<TId>(String bucketId, TId id, Int32 version)
        {
            Logger.DebugFormat("Retreiving aggregate id {0} version {1} from bucket {2} in store", id, version, bucketId);

            ISnapshot snapshot = GetSnapshot(bucketId, id, version);
            IEventStream stream = OpenStream(bucketId, id, version, snapshot);

            if (stream == null && snapshot == null) return (T)null;

            // Call the 'private' constructor
            var root = Newup(stream, _builder);
            (root as IEventSource<TId>).Id = id;
            (root as IEventSource<TId>).BucketId = bucketId;

            if (snapshot != null && root is ISnapshotting)
                ((ISnapshotting)root).RestoreSnapshot(snapshot);

            root.Hydrate(stream.Events);

            return root;
        }

        public T New<TId>(TId id)
        {
            return New<TId>(Bucket.Default, id);
        }

        public T New<TId>(String bucketId, TId id)
        {
            var stream = PrepareStream(bucketId, id);
            var root = Newup(stream, _builder);
            (root as IEventSource<TId>).Id = id;
            (root as IEventSource<TId>).BucketId = bucketId;
            return root;
        }

        private T Newup(IEventStream stream, IBuilder builder)
        {
            // Call the 'private' constructor
            var tCtor = typeof(T).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { }, null);

            if (tCtor == null)
                throw new AggregateException("Aggregate needs a PRIVATE parameterless constructor");
            var root = (T)tCtor.Invoke(null);


            // Todo: I bet there is a way to make a INeedBuilding<T> type interface
            //      and loop over each, calling builder.build for each T
            if (root is INeedStream)
                (root as INeedStream).Stream = stream;
            if (root is INeedBuilder)
                (root as INeedBuilder).Builder = builder;
            if (root is INeedEventFactory)
                (root as INeedEventFactory).EventFactory = builder.Build<IMessageCreator>();
            if (root is INeedRouteResolver)
                (root as INeedRouteResolver).Resolver = builder.Build<IRouteResolver>();
            if (root is INeedRepositoryFactory)
                (root as INeedRepositoryFactory).RepositoryFactory = builder.Build<IRepositoryFactory>();

            return root;
        }


        private ISnapshot GetSnapshot<TId>(String bucketId, TId id, int version)
        {
            ISnapshot snapshot;
            var snapshotId = String.Format("{0}::{1}/{2}.snapshots", typeof(T).FullName, bucketId, id);
            if (!_snapshots.TryGetValue(snapshotId, out snapshot))
            {
                _snapshots[snapshotId] = snapshot = _store.GetSnapshot(snapshotId, version);
            }

            return snapshot;
        }

        private IEventStream OpenStream<TId>(String bucketId, TId id, int version, ISnapshot snapshot)
        {
            IEventStream stream;
            var streamId = String.Format("{0}::{1}/{2}", typeof(T).FullName, bucketId, id);
            if (_streams.TryGetValue(streamId, out stream))
                return stream;

            if (snapshot == null)
                _streams[streamId] = stream = _store.GetStream(streamId);
            else
                _streams[streamId] = stream = _store.GetStream(streamId, snapshot.StreamRevision + 1);
            return stream;
        }

        private IEventStream PrepareStream<TId>(String bucketId, TId id)
        {
            IEventStream stream;
            var streamId = String.Format("{0}::{1}/{2}", typeof(T).FullName, bucketId, id);
            if (!_streams.TryGetValue(streamId, out stream))
                _streams[streamId] = stream = new EventStream<T>(_builder, streamId, bucketId, -1, null, null);

            return stream;
        }
    }
}