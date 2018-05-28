using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class TestablePocoRepository<T, TParent> : TestablePocoRepository<T>, IPocoRepository<T, TParent>, IPocoRepositoryTest<T, TParent> where TParent : IEntity where T : class, new()
    {
        private readonly TParent _parent;

        public TestablePocoRepository(TParent parent, TestableUnitOfWork uow)
            : base(uow)
        {
            _parent = parent;
        }
        public override Task<T> TryGet(Id id)
        {
            if (id == null) return null;

            try
            {
                return Get(id);
            }
            catch (NotFoundException) { }
            return null;
        }

        public override async Task<T> Get(Id id)
        {
            var streamId = $"{_parent.BuildParentsString()}.{id}";

            var entity = await Get(_parent.Bucket, streamId, _parent.BuildParents()).ConfigureAwait(false);
            return entity;
        }

        public override async Task<T> New(Id id)
        {
            var streamId = $"{_parent.BuildParentsString()}.{id}";

            var entity = await New(_parent.Bucket, streamId, _parent.BuildParents()).ConfigureAwait(false);
            return entity;
        }
        public override IPocoChecker Check(Id id)
        {
            return Check(_uow.MakeId(id.ToString()));
        }
        public override IPocoChecker Check(TestableId id)
        {
            var streamId = $"{_parent.BuildParentsString()}.{id}";
            return Check(_parent.Bucket, streamId, _parent.BuildParents());
        }
        public override IPocoPlanner Plan(Id id)
        {
            return Plan(_uow.MakeId(id.ToString()));
        }
        public override IPocoPlanner Plan(TestableId id)
        {
            var streamId = $"{_parent.BuildParentsString()}.{id}";
            return Plan(_parent.Bucket, streamId, _parent.BuildParents());
        }
    }
    class TestablePocoRepository<T> : IPocoRepository<T>, IPocoRepositoryTest<T> where T: class, new()
    {
        protected readonly Dictionary<Tuple<string, Id, Id[]>, Tuple<long, T, string>> Tracked = new Dictionary<Tuple<string, Id, Id[]>, Tuple<long, T, string>>();
        public readonly Dictionary<Tuple<string, Id, Id[]>, T> Pocos = new Dictionary<Tuple<string, Id, Id[]>, T>();

        protected readonly TestableUnitOfWork _uow;
        private bool _disposed;

        public int ChangedStreams =>
                // Compares the stored serialized poco against the current to determine how many changed
                Tracked.Values.Count(x => x.Item1 == EntityFactory.NewEntityVersion || JsonConvert.SerializeObject(x.Item2) != x.Item3);

        public TestablePocoRepository(TestableUnitOfWork uow)
        {
            _uow = uow;
        }


        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            Tracked.Clear();

            _disposed = true;
        }

        public virtual Task<T> TryGet(Id id)
        {
            return TryGet(Defaults.Bucket, id);
        }
        public async Task<T> TryGet(string bucket, Id id)
        {
            try
            {
                return await Get(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return null;
        }

        public virtual Task<T> Get(Id id)
        {
            return Get(Defaults.Bucket, id);
        }

        public Task<T> Get(string bucket, Id id)
        {
            return Get(bucket, id, null);
        }
        protected Task<T> Get(string bucket, Id id, Id[] parents)
        {
            var cacheId = new Tuple<string, Id, Id[]>(bucket, id, parents);
            Tuple<long, T, string> root;
            if (Tracked.TryGetValue(cacheId, out root))
                return Task.FromResult(root.Item2);

            var poco = Pocos[cacheId];

            if (poco == null)
                throw new NotFoundException($"Poco {cacheId} not found");

            // Storing the original value in the cache via SerializeObject so we can check if needs saving
            Tracked[cacheId] = new Tuple<long, T, string>(0, poco, JsonConvert.SerializeObject(poco));

            return Task.FromResult(poco);
        }

        public virtual Task<T> New(Id id)
        {
            return New(Defaults.Bucket, id);
        }

        public Task<T> New(string bucket, Id id)
        {
            return New(bucket, id, null);
        }

        protected Task<T> New(string bucket, Id id, Id[] parents)
        {
            var cacheId = new Tuple<string, Id, Id[]>(bucket, id, parents);

            if (Tracked.ContainsKey(cacheId))
                throw new InvalidOperationException($"Poco of Id {cacheId} already exists, cannot make a new one");

            var poco = new T();

            Tracked[cacheId] = new Tuple<long, T, string>(EntityFactory.NewEntityVersion, poco, JsonConvert.SerializeObject(poco));

            return Task.FromResult(poco);
        }

        public virtual IPocoChecker Check(Id id)
        {
            return Check(Defaults.Bucket, _uow.MakeId(id.ToString()));
        }
        public virtual IPocoChecker Check(TestableId id)
        {
            return Check(Defaults.Bucket, id);
        }

        public IPocoChecker Check(string bucket, Id id)
        {
            return Check(bucket, _uow.MakeId(id), null);
        }
        public IPocoChecker Check(string bucket, TestableId id)
        {
            return Check(bucket, id, null);
        }
        protected IPocoChecker Check(string bucket, Id id, Id[] parents)
        {
            return Check(bucket, _uow.MakeId(id.ToString()), parents);
        }
        protected IPocoChecker Check(string bucket, TestableId id, Id[] parents)
        {
            var cacheId = Tuple.Create(bucket, (Id)id, parents);
            if (!Tracked.ContainsKey(cacheId))
                throw new ExistException(typeof(T), bucket, id);

            return new PocoChecker<T>(this, Tracked[cacheId]);
        }

        public virtual IPocoPlanner Plan(Id id)
        {
            return Plan(Defaults.Bucket, _uow.MakeId(id));
        }
        public virtual IPocoPlanner Plan(TestableId id)
        {
            return Plan(Defaults.Bucket, id);
        }

        public IPocoPlanner Plan(string bucket, Id id)
        {
            return Plan(bucket, _uow.MakeId(id.ToString()));
        }
        public IPocoPlanner Plan(string bucket, TestableId id)
        {
            return Plan(bucket, id, null);
        }
        protected IPocoPlanner Plan(string bucket, Id id, Id[] parents)
        {
            return Plan(bucket, _uow.MakeId(id.ToString()), parents);
        }
        protected IPocoPlanner Plan(string bucket, TestableId id, Id[] parents)
        {
            return new PocoPlanner<T>(this, bucket, id, parents);
        }
    }
}
