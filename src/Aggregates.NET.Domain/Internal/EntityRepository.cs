using Aggregates.Contracts;
using Aggregates.Exceptions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Aggregates.Internal
{
    public class EntityRepository<TAggregateId, T> : Repository<T>, IEntityRepository<TAggregateId, T> where T : class, IEntity
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EntityRepository<,>));
        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapStore;
        private readonly IBuilder _builder;
        private readonly TAggregateId _aggregateId;
        private readonly IEventStream _parentStream;

        private readonly ConcurrentDictionary<String, ISnapshot> _snapshots = new ConcurrentDictionary<String, ISnapshot>();
        private readonly ConcurrentDictionary<String, IEventStream> _streams = new ConcurrentDictionary<String, IEventStream>();

        public EntityRepository(TAggregateId aggregateId, IEventStream parentStream, IBuilder builder)
            : base(builder)
        {
            _aggregateId = aggregateId;
            _parentStream = parentStream;
            _builder = builder;
            _store = _builder.Build<IStoreEvents>();
            _snapStore = builder.Build<IStoreSnapshots>();
        }
        ~EntityRepository()
        {
            Dispose(false);
        }


        public override T Get<TId>(TId id)
        {
            Logger.DebugFormat("Retreiving entity id '{0}' from aggregate '{1}' in store", id, _aggregateId);

            var snapshot = GetSnapshot(id);
            var stream = OpenStream(id, snapshot);

            if (stream == null && snapshot == null)
                throw new NotFoundException("Entity snapshot not found");
            // Get requires the stream exists
            if (stream.StreamVersion == -1)
                throw new NotFoundException("Entity stream not found");

            // Call the 'private' constructor
            var entity = Newup(stream, _builder);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEntity<TId, TAggregateId>).AggregateId = _aggregateId;

            if (snapshot != null && entity is ISnapshotting)
                ((ISnapshotting)entity).RestoreSnapshot(snapshot.Payload);

            entity.Hydrate(stream.Events.Select(e => e.Event));

            this._parentStream.AddChild(stream);
            return entity;
        }

        public override T New<TId>(TId id)
        {
            var stream = PrepareStream(id);
            var entity = Newup(stream, _builder);
            (entity as IEventSource<TId>).Id = id;

            this._parentStream.AddChild(stream);
            return entity;
        }

        public override IEnumerable<T> Query<TSnapshot, TId>(Expression<Func<TSnapshot, Boolean>> predicate)
        {
            // Queries the snapshot store for the user's predicate and returns matching entities
            return _snapStore.Query<T, TId, TSnapshot>(_parentStream.Bucket, predicate).Select(x =>
            {
                var stream = OpenStream(x.Stream, x);

                var memento = (x.Payload as IMemento<TId>);

                // Call the 'private' constructor
                var entity = Newup(stream, _builder);
                (entity as IEventSource<TId>).Id = memento.Id;
                (entity as IEntity<TId, TAggregateId>).AggregateId = _aggregateId;

                ((ISnapshotting)entity).RestoreSnapshot(x.Payload);

                entity.Hydrate(stream.Events.Select(e => e.Event));

                return entity;
            });
        }


        private ISnapshot GetSnapshot<TId>(TId id)
        {
            ISnapshot snapshot;
            var snapshotId = String.Format("{0}-{1}", _parentStream.StreamId, id);
            if (!_snapshots.TryGetValue(snapshotId, out snapshot))
            {
                _snapshots[snapshotId] = snapshot = _snapStore.GetSnapshot<T>(_parentStream.Bucket, snapshotId);
            }

            return snapshot;
        }

        private IEventStream OpenStream<TId>(TId id, ISnapshot snapshot)
        {
            var streamId = String.Format("{0}-{1}", _parentStream.StreamId, id);
            return OpenStream(streamId, snapshot);
        }
        private IEventStream OpenStream(String streamId, ISnapshot snapshot)
        {
            var cacheId = String.Format("{0}-{1}", _parentStream.Bucket, streamId);
            IEventStream stream;
            if (_streams.TryGetValue(cacheId, out stream))
                return stream;

            if (snapshot == null)
                _streams[cacheId] = stream = _store.GetStream<T>(_parentStream.Bucket, streamId);
            else
                _streams[cacheId] = stream = _store.GetStream<T>(_parentStream.Bucket, streamId, snapshot.Version + 1);
            return stream;
        }

        private IEventStream PrepareStream<TId>(TId id)
        {
            var streamId = String.Format("{0}-{1}", _parentStream.StreamId, id);
            IEventStream stream;
            var cacheId = String.Format("{0}-{1}-{2}", _parentStream.Bucket, _parentStream.StreamId, id);
            if (!_streams.TryGetValue(cacheId, out stream))
                _streams[cacheId] = stream = _store.GetStream<T>(_parentStream.Bucket, streamId);

            return stream;
        }
    }
}