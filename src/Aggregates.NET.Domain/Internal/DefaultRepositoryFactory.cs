using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    class DefaultRepositoryFactory : IRepositoryFactory
    {
        private static readonly ConcurrentDictionary<Type, Type> RepoCache = new ConcurrentDictionary<Type, Type>();

        public IRepository<T> ForAggregate<T>(IBuilder builder) where T : Aggregate<T>
        {
            var repoType = RepoCache.GetOrAdd(typeof(T), (key) => typeof(Repository<>).MakeGenericType(typeof(T)));

            return (IRepository<T>)Activator.CreateInstance(repoType, builder);
        }
        public IRepository<TParent, T> ForEntity<TParent, T>(TParent parent, IBuilder builder) where T : Entity<T, TParent> where TParent : Entity<TParent>
        {
            var repoType = RepoCache.GetOrAdd(typeof(T), (key) => typeof(Repository<,>).MakeGenericType(typeof(TParent), typeof(T)));

            return (IRepository<TParent, T>)Activator.CreateInstance(repoType, parent, builder);
            
        }
        public IPocoRepository<T> ForPoco<T>(IBuilder builder) where T : class, new()
        {
            var repoType = RepoCache.GetOrAdd(typeof(T), (key) => typeof(PocoRepository<>).MakeGenericType(typeof(T)));

            return (IPocoRepository<T>)Activator.CreateInstance(repoType, builder);
        }
        public IPocoRepository<TParent, T> ForPoco<TParent, T>(TParent parent, IBuilder builder) where T : class, new() where TParent : Entity<TParent>
        {
            var repoType = RepoCache.GetOrAdd(typeof(T), (key) => typeof(PocoRepository<,>).MakeGenericType(typeof(TParent), typeof(T)));

            return (IPocoRepository<TParent, T>)Activator.CreateInstance(repoType, parent, builder);
        }
    }
}