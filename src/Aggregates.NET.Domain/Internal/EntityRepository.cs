using System;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    public class EntityRepository<TParent, TParentId, T> : Repository<T>, IEntityRepository<TParent, TParentId, T> where T : class, IEntity where TParent : class, IBase<TParentId>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EntityRepository<,,>));
        private readonly TParent _parent;

        public EntityRepository(TParent parent, IBuilder builder)
            : base(builder)
        {
            _parent = parent;

        }

        public override async Task<T> TryGet<TId>(TId id)
        {
            if (id == null) return null;
            if (typeof(TId) == typeof(string) && string.IsNullOrEmpty(id as string)) return null;

            try
            {
                return await Get(id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }

            return null;
        }

        public override async Task<T> Get<TId>(TId id)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving entity id [{id}] from parent [{_parent.StreamId}] [{typeof(TParent).FullName}] in store");

            var entity = await Get(_parent.Bucket, id.ToString()).ConfigureAwait(false);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEntity<TId, TParent, TParentId>).Parent = _parent;
            
            return entity;
        }

        public override async Task<T> New<TId>(TId id)
        {

            var entity = await New(_parent.Bucket, id).ConfigureAwait(false);

            try
            {
                (entity as IEventSource<TId>).Id = id;
                (entity as IEntity<TId, TParent, TParentId>).Parent = _parent;
            }
            catch (NullReferenceException)
            {
                var message =
                    $"Failed to new up entity {typeof(T).FullName}, could not set parent id! Information we have indicated entity has id type <{typeof(TId).FullName}> with parent id type <{typeof(TParentId).FullName}> - please review that this is true";
                Logger.Error(message);
                throw new ArgumentException(message);
            }
            return entity;
        }


    }
}