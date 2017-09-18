
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class RepositoryFactory : IRepositoryFactory
    {
        private static readonly ConcurrentDictionary<Type, Type> RepoCache = new ConcurrentDictionary<Type, Type>();

        public IRepository<T> ForEntity<T>() where T : IEntity
        {
            var stateType = typeof(T).BaseType.GetGenericArguments()[1];
            var repoType = RepoCache.GetOrAdd(typeof(T), (key) => typeof(Repository<,>).MakeGenericType(typeof(T), stateType));

            return (IRepository<T>)Activator.CreateInstance(repoType);
        }
        public IRepository<TParent, T> ForEntity<TParent, T>(TParent parent) where T : IChildEntity<TParent> where TParent : IEntity
        {
            var stateType = typeof(T).BaseType.GetGenericArguments()[2];
            var repoType = RepoCache.GetOrAdd(typeof(T), (key) => typeof(Repository<,,>).MakeGenericType(typeof(TParent), typeof(T), stateType));

            return (IRepository<TParent, T>)Activator.CreateInstance(repoType, parent);

        }
        public IPocoRepository<T> ForPoco<T>() where T : class, new()
        {
            var repoType = RepoCache.GetOrAdd(typeof(T), (key) => typeof(PocoRepository<>).MakeGenericType(typeof(T)));

            return (IPocoRepository<T>)Activator.CreateInstance(repoType);
        }
        public IPocoRepository<TParent, T> ForPoco<TParent, T>(TParent parent) where T : class, new() where TParent : IEntity
        {
            var repoType = RepoCache.GetOrAdd(typeof(T), (key) => typeof(PocoRepository<,>).MakeGenericType(typeof(TParent), typeof(T)));

            return (IPocoRepository<TParent, T>)Activator.CreateInstance(repoType, parent);
        }
    }
}
