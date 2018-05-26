using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;

namespace Aggregates
{
    public class TestableUnitOfWork : IDomainUnitOfWork
    {
        private Dictionary<string, IRepository> _repositories;

        public TestableUnitOfWork()
        {
            _repositories = new Dictionary<string, IRepository>();
        }

        public Guid CommitId => Guid.Empty;

        public object CurrentMessage => null;
        public IDictionary<string, string> CurrentHeaders => new Dictionary<string, string>();



        IRepository<T> IDomainUnitOfWork.For<T>()
        {
            var key = typeof(T).FullName;

            var stateType = typeof(T).BaseType.GetGenericArguments()[1];
            var repoType = typeof(TestableRepository<,>).MakeGenericType(typeof(T), stateType);

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository)) return (IRepository<T>)repository;

            return (IRepository<T>)(_repositories[key] = (IRepository)Activator.CreateInstance(repoType, this));

        }

        IRepository<TEntity, TParent> IDomainUnitOfWork.For<TEntity, TParent>(TParent parent)
        {
            var key = $"{typeof(TParent).FullName}.{parent.Id}.{typeof(TEntity).FullName}";

            var stateType = typeof(TEntity).BaseType.GetGenericArguments()[1];
            var repoType = typeof(TestableRepository<,,>).MakeGenericType(typeof(TEntity), stateType, typeof(TParent));

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository))
                return (IRepository<TEntity, TParent>)repository;

            return (IRepository<TEntity, TParent>)(_repositories[key] = (IRepository)Activator.CreateInstance(repoType, this));
        }

        IPocoRepository<T> IDomainUnitOfWork.Poco<T>()
        {
            var key = typeof(T).FullName;

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository)) return (IPocoRepository<T>)repository;

            return (IPocoRepository<T>)(_repositories[key] = (IRepository)new TestablePocoRepository<T>(this));
            
        }
        IPocoRepository<T, TParent> IDomainUnitOfWork.Poco<T, TParent>(TParent parent)
        {
            var key = $"{typeof(TParent).FullName}.{parent.Id}.{typeof(T).FullName}";

            IRepository repository;
            if (_repositories.TryGetValue(key, out repository))
                return (IPocoRepository<T, TParent>)repository;

            return (IPocoRepository<T, TParent>)(_repositories[key] = (IRepository)new TestablePocoRepository<T, TParent>(parent, this));
        }

        public IRepositoryTest<TEntity> Test<TEntity>() where TEntity : IEntity
        {
            return (IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>();
        }
        public IRepositoryTest<TEntity, TParent> Test<TEntity, TParent>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IHaveEntities<TParent>
        {
            return (IRepositoryTest<TEntity, TParent>)(this as IDomainUnitOfWork).For<TEntity, TParent>(parent);
        }
    }
}
