using Aggregates.Contracts;
using EventStore.ClientAPI.Exceptions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    // Todo: Since entities no longer live 'in' the aggregate's stream, we can probably merge EntityRepository and Repository
    public class EntityRepository<TAggregateId, T> : IEntityRepository<TAggregateId, T> where T : class, IEntity
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EntityRepository<,>));
        private readonly IStoreEvents _store;
        private readonly IBuilder _builder;
        private readonly TAggregateId _aggregateId;
        private readonly IEventStream _aggregateStream;

        private readonly ConcurrentDictionary<String, ISnapshot> _snapshots = new ConcurrentDictionary<String, ISnapshot>();
        private readonly ConcurrentDictionary<String, IEventStream> _streams = new ConcurrentDictionary<String, IEventStream>();
        private Boolean _disposed;

        public EntityRepository(TAggregateId aggregateId, IEventStream aggregateStream, IBuilder builder)
        {
            _aggregateId = aggregateId;
            _aggregateStream = aggregateStream;
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
            return Get(id, Int32.MaxValue);
        }

        public T Get<TId>(TId id, int version)
        {
            Logger.DebugFormat("Retreiving entity id {0} version {1} from aggregate {2} in store", id, version, _aggregateId);

            ISnapshot snapshot = GetSnapshot(id, version);
            IEventStream stream = OpenStream(id, version, snapshot);

            if (stream == null && snapshot == null) return (T)null;

            // Call the 'private' constructor
            var entity = Newup(stream, _builder);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEventSource<TId>).BucketId = _aggregateStream.BucketId;
            (entity as IEntity<TId, TAggregateId>).AggregateId = _aggregateId;

            if (snapshot != null && entity is ISnapshotting)
                ((ISnapshotting)entity).RestoreSnapshot(snapshot);

            entity.Hydrate(stream.Events);

            return entity;
        }

        public T New<TId>(TId id)
        {
            var stream = PrepareStream(id);
            var entity = Newup(stream, _builder);
            (entity as IEventSource<TId>).Id = id;
            return entity;
        }

        private T Newup(IEventStream stream, IBuilder builder)
        {
            // Call the 'private' constructor
            var tCtor = typeof(T).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { }, null);

            if (tCtor == null)
                throw new AggregateException("Entity needs a PRIVATE parameterless constructor");
            var entity = (T)tCtor.Invoke(null);

            // Todo: I bet there is a way to make a INeedBuilding<T> type interface
            //      and loop over each, calling builder.build for each T
            if (entity is INeedStream)
                (entity as INeedStream).Stream = stream;
            if (entity is INeedBuilder)
                (entity as INeedBuilder).Builder = builder;
            if (entity is INeedEventFactory)
                (entity as INeedEventFactory).EventFactory = builder.Build<IMessageCreator>();
            if (entity is INeedRouteResolver)
                (entity as INeedRouteResolver).Resolver = builder.Build<IRouteResolver>();
            if (entity is INeedRepositoryFactory)
                (entity as INeedRepositoryFactory).RepositoryFactory = builder.Build<IRepositoryFactory>();

            return entity;
        }

        private ISnapshot GetSnapshot<TId>(TId id, int version)
        {
            ISnapshot snapshot;
            var snapshotId = String.Format("{0}//{1}::{2}.snapshots", _aggregateStream.StreamId, typeof(T).FullName, id);
            if (!_snapshots.TryGetValue(snapshotId, out snapshot))
            {
                _snapshots[snapshotId] = snapshot = _store.GetSnapshot(snapshotId, version);
            }

            return snapshot;
        }

        private IEventStream OpenStream<TId>(TId id, int version, ISnapshot snapshot)
        {
            IEventStream stream;
            var streamId = String.Format("{0}//{1}::{2}", _aggregateStream.StreamId, typeof(T).FullName, id);
            if (_streams.TryGetValue(streamId, out stream))
                return stream;

            if (snapshot == null)
                _streams[streamId] = stream = _store.GetStream(streamId);
            else
                _streams[streamId] = stream = _store.GetStream(streamId, snapshot.StreamRevision + 1);
            return stream;
        }

        private IEventStream PrepareStream<TId>(TId id)
        {
            IEventStream stream;
            var streamId = String.Format("{0}//{1}::{2}", _aggregateStream.StreamId, typeof(T).FullName, id);
            if (!_streams.TryGetValue(streamId, out stream))
                _streams[streamId] = stream = new EventStream<T>(_builder, streamId, _aggregateStream.BucketId, -1, null, null);

            return stream;
        }
    }
}