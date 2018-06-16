using Aggregates.Internal;
using Aggregates.UnitOfWork;
using Aggregates.UnitOfWork.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    class TestableApplication : ITestableApplication
    {
        private IdRegistry _ids;
        public dynamic Bag { get; set; }

        internal Dictionary<TestableId, object> Planned;
        internal Dictionary<TestableId, object> Added;
        internal Dictionary<TestableId, object> Updated;
        internal List<TestableId> Deleted;
        internal List<TestableId> Read;

        public TestableApplication(IdRegistry ids)
        {
            _ids = ids;
            Planned = new Dictionary<TestableId, object>();
            Added = new Dictionary<TestableId, object>();
            Updated = new Dictionary<TestableId, object>();
            Deleted = new List<TestableId>();
            Read = new List<TestableId>();
        }

        public Task Add<T>(Id id, T document) where T : class
        {
            Added[_ids.MakeId(id)] = document;
            return Task.CompletedTask;
        }

        public Task Delete<T>(Id id) where T : class
        {
            Deleted.Add(_ids.MakeId(id));
            return Task.CompletedTask;
        }

        public Task<T> Get<T>(Id id) where T : class
        {
            var testable = _ids.MakeId(id);
            Read.Add(testable);
            return Task.FromResult(Planned[testable] as T);
        }

        public Task<IQueryResult<T>> Query<T>(IDefinition query) where T : class
        {
            throw new NotImplementedException();
        }

        public Task<T> TryGet<T>(Id id) where T : class
        {
            var testable = _ids.MakeId(id);
            Read.Add(testable);
            if (Planned.ContainsKey(testable))
                return Task.FromResult(Planned[testable] as T);
            return Task.FromResult((T)null);
        }

        public Task Update<T>(Id id, T document) where T : class
        {
            Updated[_ids.MakeId(id)] = document;
            return Task.CompletedTask;
        }

        public IModelChecker<TModel> Check<TModel>(Id id) where TModel : class, new()
        {
            return new ModelChecker<TModel>(this, _ids, id);
        }

        public IModelChecker<TModel> Check<TModel>(TestableId id) where TModel : class, new()
        {
            return new ModelChecker<TModel>(this, _ids, id);
        }

        public IModelPlanner<TModel> Plan<TModel>(Id id) where TModel : class, new()
        {
            return new ModelPlanner<TModel>(this, _ids, id);
        }

        public IModelPlanner<TModel> Plan<TModel>(TestableId id) where TModel : class, new()
        {
            return new ModelPlanner<TModel>(this, _ids, id);
        }
    }
}
