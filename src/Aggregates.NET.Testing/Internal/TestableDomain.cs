using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.UnitOfWork;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    class TestableDomain : ITestableDomain
    {
        public readonly TestableContext Context;

        private readonly IdRegistry _ids;
        private readonly Dictionary<string, IRepository> _repositories;

        public TestableDomain(TestableContext ctx, IdRegistry ids)
        {
            Context = ctx;
            _ids = ids;
            _repositories = new Dictionary<string, IRepository>();
        }

        public Guid CommitId => Guid.Empty;
		public Guid MessageId => Guid.Empty;

		public object CurrentMessage => null;
        public IDictionary<string, string> CurrentHeaders => new Dictionary<string, string>();

        public IMutating MutateIncoming(IMutating command) { return command; }
        public IMutating MutateOutgoing(IMutating command) { return command; }

        IRepository<T> IDomainUnitOfWork.For<T>()
        {
            var key = typeof(T).FullName;

            var stateType = typeof(T).BaseType.GetGenericArguments()[1];
            var repoType = typeof(TestableRepository<,>).MakeGenericType(typeof(T), stateType);

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository)) return (IRepository<T>)repository;

            return (IRepository<T>)(_repositories[key] = (IRepository)Activator.CreateInstance(repoType, this, _ids));

        }

        IRepository<TEntity, TParent> IDomainUnitOfWork.For<TEntity, TParent>(TParent parent)
        {
            var key = $"{typeof(TParent).FullName}.{parent.Id}.{typeof(TEntity).FullName}";

            var stateType = typeof(TEntity).BaseType.GetGenericArguments()[1];
            var repoType = typeof(TestableRepository<,,>).MakeGenericType(typeof(TEntity), stateType, typeof(TParent));

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository))
                return (IRepository<TEntity, TParent>)repository;

            return (IRepository<TEntity, TParent>)(_repositories[key] = (IRepository)Activator.CreateInstance(repoType, parent, this, _ids));
        }


        public IEventChecker<TEntity> Check<TEntity>(Id id) where TEntity : IEntity
        {
            return ((IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>()).Check(id);
        }
        public IEventPlanner<TEntity> Plan<TEntity>(Id id) where TEntity : IEntity
        {
            return ((IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>()).Plan(id);
        }
        public IEventChecker<TEntity> Check<TEntity>(string bucket, Id id) where TEntity : IEntity
        {
            return ((IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>()).Check(id);
        }
        public IEventPlanner<TEntity> Plan<TEntity>(string bucket, Id id) where TEntity : IEntity
        {
            return ((IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>()).Plan(bucket, id);
        }



        internal IEventChecker<TEntity> Check<TEntity, TParent>(TParent parent, Id id) where TEntity : IEntity, IChildEntity<TParent> where TParent : IHaveEntities<TParent>
        {
            return ((IRepositoryTest<TEntity, TParent>)(this as IDomainUnitOfWork).For<TEntity, TParent>(parent)).Check(id);
        }
        internal IEventPlanner<TEntity> Plan<TEntity, TParent>(TParent parent, Id id) where TEntity : IEntity, IChildEntity<TParent> where TParent : IHaveEntities<TParent>
        {
            return ((IRepositoryTest<TEntity, TParent>)(this as IDomainUnitOfWork).For<TEntity, TParent>(parent)).Plan(id);
        }
    }
}
