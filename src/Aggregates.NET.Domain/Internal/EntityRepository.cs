using Aggregates.Contracts;
using Aggregates.Exceptions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Aggregates.Internal
{
    public class EntityRepository<TAggregateId, T> : Repository<T>, IEntityRepository<TAggregateId, T> where T : class, IEntity
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EntityRepository<,>));
        private readonly IStoreEvents _store;
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
        

        private ISnapshot GetSnapshot<TId>(TId id)
        {
            ISnapshot snapshot;
            var snapshotId = String.Format("{0}-{1}-{2}", _parentStream.StreamId, typeof(T).FullName, id);
            if (!_snapshots.TryGetValue(snapshotId, out snapshot))
            {
                _snapshots[snapshotId] = snapshot = _store.GetSnapshot<T>(_parentStream.Bucket, id.ToString());
            }

            return snapshot;
        }

        private IEventStream OpenStream<TId>(TId id, ISnapshot snapshot)
        {
            IEventStream stream;
            var streamId = String.Format("{0}-{1}-{2}", _parentStream.StreamId, typeof(T).FullName, id);
            if (_streams.TryGetValue(streamId, out stream))
                return stream;

            if (snapshot == null)
                _streams[streamId] = stream = _store.GetStream<T>(_parentStream.Bucket, id.ToString());
            else
                _streams[streamId] = stream = _store.GetStream<T>(_parentStream.Bucket, id.ToString(), snapshot.Version + 1);
            return stream;
        }

        private IEventStream PrepareStream<TId>(TId id)
        {
            IEventStream stream;
            var streamId = String.Format("{0}-{1}-{2}", _parentStream.StreamId, typeof(T).FullName, id);
            if (!_streams.TryGetValue(streamId, out stream))
                _streams[streamId] = stream = _store.GetStream<T>(_parentStream.Bucket, id.ToString());

            return stream;
        }
    }
}