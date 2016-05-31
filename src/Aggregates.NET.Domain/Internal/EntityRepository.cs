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
using System.Threading.Tasks;

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


        public override async Task<T> Get<TId>(TId id)
        {
            Logger.DebugFormat("Retreiving entity id [{0}] from aggregate [{1}] in store", id, _aggregateId);
            var streamId = String.Format("{0}.{1}", _parentStream.StreamId, id);

            var entity = await Get(_parentStream.Bucket, streamId);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEntity<TId, TAggregateId>).AggregateId = _aggregateId;
            
            return entity;
        }

        public override async Task<T> New<TId>(TId id)
        {
            var streamId = String.Format("{0}.{1}", _parentStream.StreamId, id);

            var entity = await New(_parentStream.Bucket, streamId);

            try
            {
                (entity as IEventSource<TId>).Id = id;
                (entity as IEntity<TId, TAggregateId>).AggregateId = _aggregateId;
            }
            catch (NullReferenceException)
            {
                var message = String.Format("Failed to new up entity {0}, could not set parent id! Information we have indicated entity has id type <{1}> with parent id type <{2}> - please review that this is true", typeof(T).FullName, typeof(TId).FullName, typeof(TAggregateId).FullName);
                Logger.Error(message);
                throw new ArgumentException(message);
            }
            return entity;
        }


    }
}