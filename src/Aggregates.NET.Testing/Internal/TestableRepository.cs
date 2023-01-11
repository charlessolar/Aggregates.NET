using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates.Internal
{

    [ExcludeFromCodeCoverage]
    class TestableRepository<TEntity, TState, TParent> : TestableRepository<TEntity, TState>, IRepository<TEntity, TParent>, IRepositoryTest<TEntity, TParent> where TParent : IEntity where TEntity : Entity<TEntity, TState, TParent> where TState : class, IState, new()
    {
        private readonly TParent _parent;

        public TestableRepository(TParent parent, TestableDomain uow, IdRegistry ids)
            : base(uow, ids)
        {
            _parent = parent;
        }

        public override async Task<TEntity> TryGet(Id id)
        {
            if (id == null) return default(TEntity);

            try
            {
                return await Get(id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return default(TEntity);

        }
        public override async Task<TEntity> Get(Id id)
        {
            id = _ids.MakeId(id);
            var cacheId = $"{_parent.Bucket}.{_parent.Id}.{id}";
            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await GetUntracked(_parent.Bucket, id, _parent).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }

        public override async Task<TEntity> New(Id id)
        {
            id = _ids.MakeId(id);
            var cacheId = $"{_parent.Bucket}.{_parent.Id}.{id}";

            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
            {
                root = await NewUntracked(_parent.Bucket, id, _parent).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }
        public override IEventPlanner<TEntity> Plan(Id id)
        {
            id = _ids.MakeId(id);
            return Plan((TestableId)id);
        }
        public override IEventPlanner<TEntity> Plan(TestableId id)
        {
            return new EventPlanner<TEntity, TState>(_uow, _ids, _eventstore, _snapstore, _factory, () => Get(id).Result, _parent.Bucket, id, _parent);
        }
        public override IEventChecker<TEntity> Check(Id id)
        {
            id = _ids.MakeId(id);
            return Check((TestableId)id);
        }
        public override IEventChecker<TEntity> Check(TestableId id)
        {
            var cacheId = $"{_parent.Bucket}.{_parent.Id}.{id}";
            if (!Tracked.ContainsKey(cacheId))
                throw new ExistException(typeof(TEntity), _parent.Bucket, id);
            return new EventChecker<TEntity, TState>(_uow, _ids, _factory, Tracked[cacheId]);
        }

        protected override async Task<TEntity> GetUntracked(string bucket, Id id, IEntity parent)
        {
            var entity = await base.GetUntracked(bucket, id, parent).ConfigureAwait(false);

            entity.Parent = _parent;

            return entity;
        }

        protected override async Task<TEntity> NewUntracked(string bucket, Id id, IEntity parent)
        {
            var entity = await base.NewUntracked(bucket, id, parent).ConfigureAwait(false);

            entity.Parent = _parent;

            return entity;
        }
    }
    [ExcludeFromCodeCoverage]
    class TestableRepository<TEntity, TState> : IRepository<TEntity>, IRepositoryTest<TEntity> where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private static readonly IEntityFactory<TEntity> Factory = EntityFactory.For<TEntity>();

        protected readonly ConcurrentDictionary<string, TEntity> Tracked = new ConcurrentDictionary<string, TEntity>();
        protected readonly TestableDomain _uow;
        protected readonly IdRegistry _ids;
        protected readonly TestableEventFactory _factory;
        protected readonly TestableEventStore _eventstore;
        protected readonly TestableSnapshotStore _snapstore;
        protected readonly TestableVersionRegistrar _registrar;
        private bool _disposed;

        public TestableRepository(TestableDomain uow, IdRegistry ids)
        {
            _uow = uow;
            _ids = ids;
            _factory = uow.Context.ServiceProvider.GetRequiredService<TestableEventFactory>();
            _eventstore = uow.Context.ServiceProvider.GetRequiredService<TestableEventStore>();
            _snapstore = uow.Context.ServiceProvider.GetRequiredService<TestableSnapshotStore>();
            _registrar = uow.Context.ServiceProvider.GetRequiredService<TestableVersionRegistrar>();
        }

        public int ChangedStreams => Tracked.Count(x => x.Value.Dirty);

        private IParentDescriptor[] getParents(IEntity entity)
        {
            if (entity == null || !(entity is IChildEntity))
                return null;

            var parents = getParents((entity as IChildEntity).Parent)?.ToList() ?? new List<IParentDescriptor>();
            parents.Add(new ParentDescriptor { EntityType = _registrar.GetVersionedName(entity.GetType()), StreamId = entity.Id });
            return parents.ToArray();
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

        public virtual Task<TEntity> Get(Id id)
        {
            return Get(Defaults.Bucket, id);
        }

        public async Task<TEntity> Get(string bucket, Id id)
        {
            id = _ids.MakeId(id);
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
        protected virtual async Task<TEntity> GetUntracked(string bucket, Id id, IEntity parent = null)
        {
            id = _ids.MakeId(id);
            var snapshot = await _snapstore.GetSnapshot<TEntity, TState>(bucket, id, parent.GetParentIds()).ConfigureAwait(false);
            var events = await _eventstore.GetEvents<TEntity>(StreamDirection.Forwards, bucket, id, parent.GetParentIds(), start: snapshot?.Version).ConfigureAwait(false);

            var entity = Factory.Create(new NullLogger<TestableRepository<TEntity, TState>>(), bucket, id, getParents(parent), events.Select(x => x.Event as Messages.IEvent).ToArray(), snapshot?.Payload);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedVersionRegistrar).Registrar = _registrar;

            return entity;
        }

        public virtual Task<TEntity> New(Id id)
        {
            return New(Defaults.Bucket, id);
        }

        public async Task<TEntity> New(string bucket, Id id)
        {
            id = _ids.MakeId(id);
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
        protected virtual Task<TEntity> NewUntracked(string bucket, Id id, IEntity parent = null)
        {
            // If the test wants to NEW an existing stream, mimic what would happen (AlreadyExistsException)
            if (_eventstore.StreamExists<TEntity>(bucket, id, parent.GetParentIds()))
                throw new EntityAlreadyExistsException(typeof(TEntity).FullName, bucket, id, parent.GetParentIds());

            id = _ids.MakeId(id);
            var entity = Factory.Create(new NullLogger<TestableRepository<TEntity, TState>>(), bucket, id, getParents(parent));

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedVersionRegistrar).Registrar = _registrar;

            return Task.FromResult(entity);
        }

        public virtual Task<TEntity> TryGet(Id id)
        {
            return TryGet(Defaults.Bucket, id);
        }
        public async Task<TEntity> TryGet(string bucket, Id id)
        {
            if (id == null)
                return default(TEntity);

            id = _ids.MakeId(id);
            try
            {
                return await Get(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return default(TEntity);
        }

        public virtual IEventPlanner<TEntity> Plan(Id id)
        {
            id = _ids.MakeId(id);
            return Plan(Defaults.Bucket, (TestableId)id);
        }
        public virtual IEventPlanner<TEntity> Plan(TestableId id)
        {
            return Plan(Defaults.Bucket, id);
        }
        public IEventPlanner<TEntity> Plan(string bucket, Id id)
        {
            id = _ids.MakeId(id);
            return Plan(bucket, (TestableId)id);
        }
        public IEventPlanner<TEntity> Plan(string bucket, TestableId id)
        {
            //                                                                                       async method isnt async so hack it
            return new EventPlanner<TEntity, TState>(_uow, _ids, _eventstore, _snapstore, _factory, () => Get(bucket, id).Result, bucket, id);
        }
        public virtual IEventChecker<TEntity> Check(Id id)
        {
            id = _ids.MakeId(id);
            return Check(Defaults.Bucket, (TestableId)id);
        }
        public virtual IEventChecker<TEntity> Check(TestableId id)
        {
            return Check(Defaults.Bucket, id);
        }
        public IEventChecker<TEntity> Check(string bucket, Id id)
        {
            id = _ids.MakeId(id);
            return Check(bucket, (TestableId)id);
        }
        public IEventChecker<TEntity> Check(string bucket, TestableId id)
        {
            var cacheId = $"{bucket}.{id}";
            if (!Tracked.ContainsKey(cacheId))
                throw new ExistException(typeof(TEntity), bucket, id);
            return new EventChecker<TEntity, TState>(_uow, _ids, _factory, Tracked[cacheId]);
        }

    }
}
