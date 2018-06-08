
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Aggregates.Contracts;
using Aggregates.Extensions;

namespace Aggregates.Internal
{
    class RepositoryFactory : IRepositoryFactory
    {
        private static readonly ConcurrentDictionary<Type, object> Factories = new ConcurrentDictionary<Type, object>();

        private readonly IStoreEntities _store;

        public RepositoryFactory(IStoreEntities store)
        {
            _store = store;
        }

        public IRepository<TEntity> ForEntity<TEntity>() where TEntity : IEntity
        {
            var factory = Factories.GetOrAdd(typeof(TEntity), t => ReflectionExtensions.BuildRepositoryFunc<TEntity>()) as Func<IStoreEntities, IRepository<TEntity>>;
            if (factory == null)
                throw new InvalidOperationException("unknown entity repository");

            return factory(_store);
        }
        public IRepository<TEntity, TParent> ForEntity<TEntity, TParent>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IEntity
        {
            var factory = Factories.GetOrAdd(typeof(TEntity), t => ReflectionExtensions.BuildParentRepositoryFunc<TEntity, TParent>()) as Func<TParent, IStoreEntities, IRepository<TEntity, TParent>>;
            if (factory == null)
                throw new InvalidOperationException("unknown entity repository");

            return factory(parent, _store);

        }
    }
}
