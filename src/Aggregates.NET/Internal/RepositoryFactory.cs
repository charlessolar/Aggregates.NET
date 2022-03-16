
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class RepositoryFactory : IRepositoryFactory
    {
        private static readonly ConcurrentDictionary<Type, object> Factories = new ConcurrentDictionary<Type, object>();

        private readonly ILoggerFactory _logFactory;
        private readonly IStoreEntities _store;

        public RepositoryFactory(ILoggerFactory logFactory, IStoreEntities store)
        {
            _logFactory = logFactory;
            _store = store;
        }

        public IRepository<TEntity> ForEntity<TEntity>() where TEntity : IEntity
        {
            var factory = Factories.GetOrAdd(typeof(TEntity), t => ReflectionExtensions.BuildRepositoryFunc<TEntity>()) as Func<ILogger, IStoreEntities, IRepository<TEntity>>;
            if (factory == null)
                throw new InvalidOperationException("unknown entity repository");

            return factory(_logFactory.CreateLogger<IRepository<TEntity>>(), _store);
        }
        public IRepository<TEntity, TParent> ForEntity<TEntity, TParent>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IEntity
        {
            var factory = Factories.GetOrAdd(typeof(TEntity), t => ReflectionExtensions.BuildParentRepositoryFunc<TEntity, TParent>()) as Func<ILogger, TParent, IStoreEntities, IRepository<TEntity, TParent>>;
            if (factory == null)
                throw new InvalidOperationException("unknown entity repository");

            return factory(_logFactory.CreateLogger<IRepository<TEntity, TParent>>(), parent, _store);

        }
    }
}
