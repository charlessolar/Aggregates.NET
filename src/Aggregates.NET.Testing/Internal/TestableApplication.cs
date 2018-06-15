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

        internal Dictionary<Id, object> Planned;
        internal Dictionary<Id, object> Added;
        internal Dictionary<Id, object> Updated;
        internal List<Id> Deleted;
        internal List<Id> Read;

        public TestableApplication(IdRegistry ids)
        {
            _ids = ids;
        }

        public Task Add<T>(Id id, T document) where T : class
        {
            Added[id] = document;
            return Task.CompletedTask;
        }

        public Task Delete<T>(Id id) where T : class
        {
            Deleted.Add(id);
            return Task.CompletedTask;
        }

        public Task<T> Get<T>(Id id) where T : class
        {
            Read.Add(id);
            return Task.FromResult(Planned[id] as T);
        }

        public Task<IQueryResult<T>> Query<T>(IDefinition query) where T : class
        {
            throw new NotImplementedException();
        }

        public Task<T> TryGet<T>(Id id) where T : class
        {
            Read.Add(id);
            if (Planned.ContainsKey(id))
                return Task.FromResult(Planned[id] as T);
            return Task.FromResult((T)null);
        }

        public Task Update<T>(Id id, T document) where T : class
        {
            Updated[id] = document;
            return Task.CompletedTask;
        }

        public IModelChecker<TModel> Check<TModel>(Id id) where TModel : class, new()
        {
            return new ModelChecker<TModel>(this, id);
        }

        public IModelPlanner<TModel> Plan<TModel>(Id id) where TModel : class, new()
        {
            return new ModelPlanner<TModel>(this, id);
        }
    }
}
