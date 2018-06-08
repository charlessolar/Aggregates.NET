using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;

namespace Aggregates
{
    public class TestableUnitOfWork : IDomainUnitOfWork
    {
        private int _longIdCounter = -1;
        public IReadOnlyDictionary<string, TestableId> GeneratedIds => _generatedIds;

        private readonly Dictionary<string, TestableId> _generatedIds = new Dictionary<string, TestableId>();
        private Dictionary<string, IRepository> _repositories;

        public TestableUnitOfWork()
        {
            _repositories = new Dictionary<string, IRepository>();
        }

        public TestableId AnyId()
        {
            var generated = new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)-_longIdCounter });
            var id = new TestableId(Constants.GeneratedAnyId, _longIdCounter--, generated.ToString(), generated);
            if (_generatedIds.ContainsKey(id.GeneratedIdKey))
                return _generatedIds[id.GeneratedIdKey];
            return _generatedIds[id.GeneratedIdKey] = id;
        }
        public TestableId MakeId(int key)
        {
            var generated = new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)-_longIdCounter });
            var id = new TestableId(Constants.GeneratedNumberedId(key), _longIdCounter--, generated.ToString(), generated);
            if (_generatedIds.ContainsKey(id.GeneratedIdKey))
                return _generatedIds[id.GeneratedIdKey];
            return _generatedIds[id.GeneratedIdKey] = id;
        }
        public TestableId MakeId(string key)
        {
            var generated = new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)-_longIdCounter });
            var id = new TestableId(Constants.GenerateNamedId(key), _longIdCounter--, generated.ToString(), generated);
            if (_generatedIds.ContainsKey(id.GeneratedIdKey))
                return _generatedIds[id.GeneratedIdKey];
            return _generatedIds[id.GeneratedIdKey] = id;
        }
        public TestableId MakeId(Id id)
        {
            if (id is TestableId)
                return (TestableId)id;

            // checks if the Id we get was a generated one
            var existing = _generatedIds.Values.FirstOrDefault(kv => kv.Equals(id));
            if(existing != null)
                return existing;

            var generated = new Guid(0, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, (byte)-_longIdCounter });
            var testable = new TestableId(Constants.GenerateNamedId(id.ToString()), _longIdCounter--, generated.ToString(), generated);
            if (_generatedIds.ContainsKey(testable.GeneratedIdKey))
                return _generatedIds[testable.GeneratedIdKey];
            return _generatedIds[testable.GeneratedIdKey] = testable;
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

            return (IRepository<TEntity, TParent>)(_repositories[key] = (IRepository)Activator.CreateInstance(repoType, parent, this));
        }


        public IChecker<TEntity> Check<TEntity>(Id id) where TEntity : IEntity
        {
            return ((IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>()).Check(id);
        }
        public IEventPlanner<TEntity> Plan<TEntity>(Id id) where TEntity : IEntity
        {
            return ((IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>()).Plan(id);
        }
        public IChecker<TEntity> Check<TEntity>(string bucket, Id id) where TEntity : IEntity
        {
            return ((IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>()).Check(id);
        }
        public IEventPlanner<TEntity> Plan<TEntity>(string bucket, Id id) where TEntity : IEntity
        {
            return ((IRepositoryTest<TEntity>)(this as IDomainUnitOfWork).For<TEntity>()).Plan(bucket, id);
        }



        internal IChecker<TEntity> Check<TEntity, TParent>(TParent parent, Id id) where TEntity : IEntity, IChildEntity<TParent> where TParent : IHaveEntities<TParent>
        {
            return ((IRepositoryTest<TEntity, TParent>)(this as IDomainUnitOfWork).For<TEntity, TParent>(parent)).Check(id);
        }
        internal IEventPlanner<TEntity> Plan<TEntity, TParent>(TParent parent, Id id) where TEntity : IEntity, IChildEntity<TParent> where TParent : IHaveEntities<TParent>
        {
            return ((IRepositoryTest<TEntity, TParent>)(this as IDomainUnitOfWork).For<TEntity, TParent>(parent)).Plan(id);
        }
    }
}
