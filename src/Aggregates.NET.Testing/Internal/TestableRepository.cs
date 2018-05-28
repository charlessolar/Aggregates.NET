using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using Newtonsoft.Json;
using NServiceBus.MessageInterfaces.MessageMapper.Reflection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{

    class TestableRepository<TEntity, TState, TParent> : TestableRepository<TEntity, TState>, IRepository<TEntity, TParent>, IRepositoryTest<TEntity, TParent> where TParent : IEntity where TEntity : Entity<TEntity, TState, TParent> where TState : class, IState, new()
    {
        private readonly TParent _parent;

        public TestableRepository(TParent parent, TestableUnitOfWork uow)
            : base(uow)
        {
            _parent = parent;
        }

        public override async Task<TEntity> TryGet(Id id)
        {
            if (id == null) return default(TEntity);
            id = _uow.MakeId(id);
            try
            {
                return await Get(id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return default(TEntity);

        }
        public override async Task<TEntity> Get(Id id)
        {
            id = _uow.MakeId(id);
            var cacheId = $"{_parent.Bucket}.{_parent.BuildParentsString()}.{id}";
            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await GetUntracked(_parent.Bucket, id, _parent.BuildParents()).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }

        public override async Task<TEntity> New(Id id)
        {
            id = _uow.MakeId(id);
            var cacheId = $"{_parent.Bucket}.{_parent.BuildParentsString()}.{id}";

            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await NewUntracked(_parent.Bucket, id, _parent.BuildParents()).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }
        public override IEventPlanner<TEntity> Plan(Id id)
        {
            id = _uow.MakeId(id);
            return Plan((TestableId)id);
        }
        public override IEventPlanner<TEntity> Plan(TestableId id)
        {
            return new EventPlanner<TEntity, TState>(_uow, _eventstore, _snapstore, _factory, () => Get(id).Result, _parent.Bucket, id, _parent.BuildParents());
        }
        public override IChecker<TEntity> Check(Id id)
        {
            id = _uow.MakeId(id);
            return Check((TestableId)id);
        }
        public override IChecker<TEntity> Check(TestableId id)
        {
            var cacheId = $"{_parent.Bucket}.{_parent.BuildParentsString()}.{id}";
            if (!Tracked.ContainsKey(cacheId))
                throw new ExistException(typeof(TEntity), _parent.Bucket, id);
            return new Checker<TEntity, TState>(_uow, _factory, Tracked[cacheId]);
        }

        protected override async Task<TEntity> GetUntracked(string bucket, Id id, Id[] parents)
        {
            id = _uow.MakeId(id);
            var entity = await base.GetUntracked(bucket, id, parents).ConfigureAwait(false);

            entity.Parent = _parent;

            return entity;
        }

        protected override async Task<TEntity> NewUntracked(string bucket, Id id, Id[] parents)
        {
            id = _uow.MakeId(id);
            var entity = await base.NewUntracked(bucket, id, parents).ConfigureAwait(false);

            entity.Parent = _parent;

            return entity;
        }
    }
    class TestableRepository<TEntity, TState> : IRepository<TEntity>, IRepositoryTest<TEntity> where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private static readonly IEntityFactory<TEntity> Factory = EntityFactory.For<TEntity>();

        protected readonly ConcurrentDictionary<string, TEntity> Tracked = new ConcurrentDictionary<string, TEntity>();
        protected readonly TestableUnitOfWork _uow;
        protected readonly TestableEventFactory _factory;
        protected readonly TestableOobWriter _oobStore;
        protected readonly TestableEventStore _eventstore;
        protected readonly TestableSnapshotStore _snapstore;
        private bool _disposed;

        public TestableRepository(TestableUnitOfWork uow)
        {
            _uow = uow;
            _factory = new TestableEventFactory(new MessageMapper());
            _oobStore = new TestableOobWriter();
            _eventstore = new TestableEventStore(uow);
            _snapstore = new TestableSnapshotStore();
        }

        public int ChangedStreams => Tracked.Count(x => x.Value.Dirty);

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

        public virtual Task<TEntity> Get(Id id)
        {
            id = _uow.MakeId(id);
            return Get(Defaults.Bucket, id);
        }

        public async Task<TEntity> Get(string bucket, Id id)
        {
            id = _uow.MakeId(id);
            var cacheId = $"{bucket}.{id}";
            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await GetUntracked(bucket, id).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }
        protected virtual async Task<TEntity> GetUntracked(string bucket, Id id, Id[] parents = null)
        {
            id = _uow.MakeId(id);
            var snapshot = await _snapstore.GetSnapshot<TEntity>(bucket, id, parents).ConfigureAwait(false);
            var events = await _eventstore.GetEvents<TEntity>(bucket, id, parents, start: snapshot?.Version).ConfigureAwait(false);

            var entity = Factory.Create(bucket, id, parents, events.Select(x => x.Event as Messages.IEvent).ToArray(), snapshot?.Payload);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobStore;

            return entity;
        }

        public virtual Task<TEntity> New(Id id)
        {
            id = _uow.MakeId(id);
            return New(Defaults.Bucket, id);
        }

        public async Task<TEntity> New(string bucket, Id id)
        {
            id = _uow.MakeId(id);
            TEntity root;
            var cacheId = $"{bucket}.{id}";
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await NewUntracked(bucket, id).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }
            return root;
        }
        protected virtual Task<TEntity> NewUntracked(string bucket, Id id, Id[] parents = null)
        {
            id = _uow.MakeId(id);
            var entity = Factory.Create(bucket, id, parents);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobStore;

            return Task.FromResult(entity);
        }

        public virtual Task<TEntity> TryGet(Id id)
        {
            id = _uow.MakeId(id);
            return TryGet(Defaults.Bucket, id);
        }
        public async Task<TEntity> TryGet(string bucket, Id id)
        {
            if (id == null) return default(TEntity);

            id = _uow.MakeId(id);
            try
            {
                return await Get(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return default(TEntity);
        }

        public virtual IEventPlanner<TEntity> Plan(Id id)
        {
            id = _uow.MakeId(id);
            return Plan(Defaults.Bucket, (TestableId)id);
        }
        public virtual IEventPlanner<TEntity> Plan(TestableId id)
        {
            return Plan(Defaults.Bucket, id);
        }
        public IEventPlanner<TEntity> Plan(string bucket, Id id)
        {
            id = _uow.MakeId(id);
            return Plan(bucket, (TestableId)id);
        }
        public IEventPlanner<TEntity> Plan(string bucket, TestableId id)
        {
            //                                                                                       async method isnt async so hack it
            return new EventPlanner<TEntity, TState>(_uow, _eventstore, _snapstore, _factory, () => Get(bucket, id).Result, bucket, id);
        }
        public virtual IChecker<TEntity> Check(Id id)
        {
            id = _uow.MakeId(id);
            return Check(Defaults.Bucket, (TestableId)id);
        }
        public virtual IChecker<TEntity> Check(TestableId id)
        {
            return Check(Defaults.Bucket, id);
        }
        public IChecker<TEntity> Check(string bucket, Id id)
        {
            id = _uow.MakeId(id);
            return Check(bucket, (TestableId)id);
        }
        public IChecker<TEntity> Check(string bucket, TestableId id)
        {
            var cacheId = $"{bucket}.{id}";
            if (!Tracked.ContainsKey(cacheId))
                throw new ExistException(typeof(TEntity), bucket, id);
            return new Checker<TEntity, TState>(_uow, _factory, Tracked[cacheId]);
        }

    }
}
