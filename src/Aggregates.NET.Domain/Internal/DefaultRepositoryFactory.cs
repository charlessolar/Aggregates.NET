using System;
using System.Collections.Generic;
using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    class DefaultRepositoryFactory : IRepositoryFactory
    {
        private static readonly IDictionary<Type, Type> RepoCache = new Dictionary<Type, Type>();

        public IRepository<T> ForAggregate<T>(IBuilder builder) where T : class, IAggregate
        {
            Type repoType;
            if (!RepoCache.TryGetValue(typeof(T), out repoType))
                repoType = RepoCache[typeof(T)] = typeof(Repository<>).MakeGenericType(typeof(T));

            return (IRepository<T>)Activator.CreateInstance(repoType, builder);
        }
        public IEntityRepository<TParent, TParentId, T> ForEntity<TParent, TParentId, T>(TParent parent, IBuilder builder) where T : class, IEntity where TParent : class, IBase<TParentId>
        {
            // Is it possible to have an entity type with multiple different types of parents?  Nope
            Type repoType;
            if (!RepoCache.TryGetValue(typeof(T), out repoType))
                repoType = RepoCache[typeof(T)] = typeof(EntityRepository<,,>).MakeGenericType(typeof(TParent), typeof(TParentId), typeof(T));

            return (IEntityRepository<TParent, TParentId, T>)Activator.CreateInstance(repoType, parent, builder);
            
        }
        public IPocoRepository<T> ForPoco<T>(IBuilder builder) where T : class, new()
        {
            Type repoType;
            if (!RepoCache.TryGetValue(typeof(T), out repoType))
                repoType = RepoCache[typeof(T)] = typeof(PocoRepository<>).MakeGenericType(typeof(T));

            return (IPocoRepository<T>)Activator.CreateInstance(repoType, builder);
        }
        public IPocoRepository<TParent, TParentId, T> ForPoco<TParent, TParentId, T>(TParent parent, IBuilder builder) where T : class, new() where TParent : class, IBase<TParentId>
        {
            Type repoType;
            if (!RepoCache.TryGetValue(typeof(T), out repoType))
                repoType = RepoCache[typeof(T)] = typeof(PocoRepository<,,>).MakeGenericType(typeof(TParent), typeof(TParentId), typeof(T));

            return (IPocoRepository<TParent, TParentId, T>)Activator.CreateInstance(repoType, parent, builder);
        }
    }
}