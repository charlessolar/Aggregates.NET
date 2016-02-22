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
        private readonly IStoreSnapshots _snapstore;
        private readonly IBuilder _builder;
        private readonly TAggregateId _aggregateId;
        private readonly IEventStream _parentStream;
        
        public EntityRepository(TAggregateId aggregateId, IEventStream parentStream, IBuilder builder)
            : base(builder)
        {
            _aggregateId = aggregateId;
            _parentStream = parentStream;
            _builder = builder;
            _store = _builder.Build<IStoreEvents>();
            _snapstore = _builder.Build<IStoreSnapshots>();
        }


        public override T Get<TId>(TId id)
        {
            Logger.DebugFormat("Retreiving entity id '{0}' from aggregate '{1}' in store", id, _aggregateId);
            var streamId = $"{_parentStream.StreamId}.{id}";

            var entity = Get(_parentStream.Bucket, streamId);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEntity<TId, TAggregateId>).AggregateId = _aggregateId;
            
            this._parentStream.AddChild(entity.Stream);
            return entity;
        }

        public override T New<TId>(TId id)
        {
            var streamId = $"{_parentStream.StreamId}.{id}";

            var stream = OpenStream(_parentStream.Bucket, streamId);
            var entity = Newup(stream, _builder);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEntity<TId, TAggregateId>).AggregateId = _aggregateId;

            this._parentStream.AddChild(stream);
            return entity;
        }
        
        
    }
}