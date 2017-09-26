
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

        private readonly IMetrics _metrics;
        private readonly IStoreEvents _eventstore;
        private readonly IStoreSnapshots _snapstore;
        private readonly IStorePocos _pocostore;
        private readonly IEventFactory _factory;

        public RepositoryFactory(IMetrics metrics, IStoreEvents eventstore, IStoreSnapshots snapstore, IStorePocos pocostore, IEventFactory factory)
        {
            _metrics = metrics;
            _eventstore = eventstore;
            _snapstore = snapstore;
            _pocostore = pocostore;
            _factory = factory;
        }

        public IRepository<TEntity> ForEntity<TEntity>(IDomainUnitOfWork uow) where TEntity : IEntity
        {
            var factory = Factories.GetOrAdd(typeof(TEntity), t => ReflectionExtensions.BuildRepositoryFunc<TEntity>()) as Func<IMetrics, IStoreEvents, IStoreSnapshots, IEventFactory, IDomainUnitOfWork, IRepository<TEntity>>;
            if (factory == null)
                throw new InvalidOperationException("unknown entity repository");

            return factory(_metrics, _eventstore, _snapstore, _factory, uow);
        }
        public IRepository<TEntity, TParent> ForEntity<TEntity, TParent>(TParent parent, IDomainUnitOfWork uow) where TEntity : IChildEntity<TParent> where TParent : IEntity
        {
            var factory = Factories.GetOrAdd(typeof(TEntity), t => ReflectionExtensions.BuildParentRepositoryFunc<TEntity, TParent>()) as Func<TParent, IMetrics, IStoreEvents, IStoreSnapshots, IEventFactory, IDomainUnitOfWork, IRepository<TEntity, TParent>>;
            if (factory == null)
                throw new InvalidOperationException("unknown entity repository");

            return factory(parent, _metrics, _eventstore, _snapstore, _factory, uow);

        }
        public IPocoRepository<T> ForPoco<T>(IDomainUnitOfWork uow) where T : class, new()
        {
            var factory = Factories.GetOrAdd(typeof(T), t => ReflectionExtensions.BuildPocoRepositoryFunc<T>()) as Func<IMetrics, IStoreEvents, IStoreSnapshots, IDomainUnitOfWork, IPocoRepository<T>>;
            if (factory == null)
                throw new InvalidOperationException("unknown entity repository");

            return factory(_metrics, _eventstore, _snapstore, uow);
        }
        public IPocoRepository<T, TParent> ForPoco<T, TParent>(TParent parent, IDomainUnitOfWork uow) where T : class, new() where TParent : IEntity
        {
            var factory = Factories.GetOrAdd(typeof(T), t => ReflectionExtensions.BuildParentPocoRepositoryFunc<T, TParent>()) as Func<TParent, IMetrics, IStoreEvents, IStoreSnapshots, IDomainUnitOfWork, IPocoRepository<T, TParent>>;
            if (factory == null)
                throw new InvalidOperationException("unknown entity repository");

            return factory(parent, _metrics, _eventstore, _snapstore, uow);
        }
    }
}
