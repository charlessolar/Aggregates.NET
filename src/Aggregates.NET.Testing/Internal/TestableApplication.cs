using Aggregates.Internal;
using Aggregates.UnitOfWork.Query;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates
{
    class TestableApplication : ITestableApplication
    {
        private readonly IdRegistry _ids;

        internal Dictionary<Tuple<Type, TestableId>, object> Planned;
        internal Dictionary<Tuple<Type, TestableId>, object> Added;
        internal Dictionary<Tuple<Type, TestableId>, object> Updated;
        internal List<Tuple<Type, TestableId>> Deleted;
        internal List<Tuple<Type, TestableId>> Read;

        public TestableApplication(IdRegistry ids)
        {
            _ids = ids;
            Planned = new Dictionary<Tuple<Type, TestableId>, object>();
            Added = new Dictionary<Tuple<Type, TestableId>, object>();
            Updated = new Dictionary<Tuple<Type, TestableId>, object>();
            Deleted = new List<Tuple<Type, TestableId>>();
            Read = new List<Tuple<Type, TestableId>>();
        }

        public Task Add<T>(Id id, T document) where T : class
        {
            Added[Tuple.Create(typeof(T), _ids.MakeId(id))] = document;
            return Task.CompletedTask;
        }

        public Task Delete<T>(Id id) where T : class
        {
            Deleted.Add(Tuple.Create(typeof(T), _ids.MakeId(id)));
            return Task.CompletedTask;
        }

        public Task<T> Get<T>(Id id) where T : class
        {
            // Getting an unplanned model doesn't need to throw for testing
            return TryGet<T>(id);
        }

        public Task<IQueryResult<T>> Query<T>(IDefinition query) where T : class
        {
            throw new NotImplementedException();
        }

        public Task<T> TryGet<T>(Id id) where T : class
        {
            var testable = Tuple.Create(typeof(T), _ids.MakeId(id));
            Read.Add(testable);
            if (Planned.ContainsKey(testable))
                return Task.FromResult(Planned[testable] as T);
            return Task.FromResult<T>(null);
        }

        public Task Update<T>(Id id, T document) where T : class
        {
            Updated[Tuple.Create(typeof(T), _ids.MakeId(id))] = document;
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
