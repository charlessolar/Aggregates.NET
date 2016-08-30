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
    public class EntityRepository<TParent, TParentId, T> : Repository<T>, IEntityRepository<TParent, TParentId, T> where T : class, IEntity where TParent : class, IBase<TParentId>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EntityRepository<,,>));
        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapstore;
        private readonly IBuilder _builder;
        private readonly TParent _parent;

        public EntityRepository(TParent parent, IBuilder builder)
            : base(builder)
        {
            _parent = parent;
            _builder = builder;
            _snapstore = _builder.Build<IStoreSnapshots>();
            _store = _builder.Build<IStoreEvents>();
            _store.Builder = _builder;

        }

        public override async Task<T> TryGet<TId>(TId id)
        {
            if (id == null) return null;
            if (typeof(TId) == typeof(String) && String.IsNullOrEmpty(id as String)) return null;
            try
            {
                return await Get<TId>(id);
            }
            catch (NotFoundException) { }
            catch (System.AggregateException e)
            {
                if (!(e.InnerException is NotFoundException) && !e.InnerExceptions.Any(x => x is NotFoundException))
                    throw;
            }
            return null;
        }

        public override async Task<T> Get<TId>(TId id)
        {
            Logger.DebugFormat("Retreiving entity id [{0}] from parent {2} [{1}] in store", id, _parent.StreamId, typeof(TParent).FullName);
            var streamId = String.Format("{0}.{1}", _parent.StreamId, id);

            var entity = await Get(_parent.Bucket, streamId);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEntity<TId, TParent, TParentId>).Parent = _parent;
            
            return entity;
        }

        public override async Task<T> New<TId>(TId id)
        {
            var streamId = String.Format("{0}.{1}", _parent.StreamId, id);

            var entity = await New(_parent.Bucket, streamId);

            try
            {
                (entity as IEventSource<TId>).Id = id;
                (entity as IEntity<TId, TParent, TParentId>).Parent = _parent;
            }
            catch (NullReferenceException)
            {
                var message = String.Format("Failed to new up entity {0}, could not set parent id! Information we have indicated entity has id type <{1}> with parent id type <{2}> - please review that this is true", typeof(T).FullName, typeof(TId).FullName, typeof(TParentId).FullName);
                Logger.Error(message);
                throw new ArgumentException(message);
            }
            return entity;
        }


    }
}