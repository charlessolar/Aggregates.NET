using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Attributes;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;
using Aggregates.Messages;
using AggregateException = System.AggregateException;

namespace Aggregates.Internal
{
    class Repository<TEntity, TState, TParent> : Repository<TEntity, TState>, IRepository<TEntity, TParent> where TParent : IEntity where TEntity : Entity<TEntity, TState, TParent> where TState : IState, new()
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Repository");

        private readonly TParent _parent;

        public Repository(TParent parent, IMetrics metrics, IStoreEvents store, IStoreSnapshots snapshots, IEventFactory factory, IDomainUnitOfWork uow)
            : base(metrics, store, snapshots, factory, uow)
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
            var cacheId = $"{_parent.Bucket}.{_parent.BuildParentsString()}.{id}";
            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
                Tracked[cacheId] = root = await GetUntracked(_parent.Bucket, id, _parent.BuildParents()).ConfigureAwait(false);

            return root;
        }

        public override async Task<TEntity> New(Id id)
        {
            var cacheId = $"{_parent.Bucket}.{_parent.BuildParentsString()}.{id}";

            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
                Tracked[cacheId] = root = await NewUntracked(_parent.Bucket, id, _parent.BuildParents()).ConfigureAwait(false);

            return root;
        }

        protected override async Task<TEntity> GetUntracked(string bucket, Id id, Id[] parents)
        {
            var entity = await base.GetUntracked(bucket, id, parents);

            entity.Parent = _parent;

            return entity;
        }

        protected override async Task<TEntity> NewUntracked(string bucket, Id id, Id[] parents)
        {
            var entity = await base.NewUntracked(bucket, id, parents);

            entity.Parent = _parent;

            return entity;
        }
    }
    class Repository<TEntity, TState> : IRepository<TEntity>, IRepository where TEntity : Entity<TEntity, TState> where TState : IState, new()
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Repository");
        private static readonly IEntityFactory<TEntity> Factory = EntityFactory.For<TEntity>();

        private static OptimisticConcurrencyAttribute _conflictResolution;

        protected readonly IDictionary<string, TEntity> Tracked = new Dictionary<string, TEntity>();
        protected readonly IMetrics _metrics;
        private readonly IStoreEvents _eventstore;
        private readonly IStoreSnapshots _snapstore;
        private readonly IEventFactory _factory;
        protected readonly IDomainUnitOfWork _uow;

        private bool _disposed;

        public int ChangedStreams => Tracked.Count(x => x.Value.Dirty);

        public Repository(IMetrics metrics, IStoreEvents store, IStoreSnapshots snapshots, IEventFactory factory, IDomainUnitOfWork uow)
        {
            _metrics = metrics;
            _eventstore = store;
            _snapstore = snapshots;
            _factory = factory;
            _uow = uow;

            // Conflict resolution is strong by default
            if (_conflictResolution == null)
                _conflictResolution = (OptimisticConcurrencyAttribute)Attribute.GetCustomAttribute(typeof(TEntity), typeof(OptimisticConcurrencyAttribute))
                                      ?? new OptimisticConcurrencyAttribute(ConcurrencyConflict.Throw);
        }
        Task IRepository.Prepare(Guid commitId)
        {
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(TEntity).FullName} starting prepare {commitId}");

            // Verify streams we read but didn't change are still save version
            return
                Tracked.Values
                    .Where(x => !x.Dirty)
                    .ToArray()
                    .WhenAllAsync((x) => _eventstore.VerifyVersion<TEntity>(x.Bucket, x.Id, x.Parents, x.Version));
        }


        async Task IRepository.Commit(Guid commitId, IDictionary<string, string> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(TEntity).FullName} starting commit {commitId}");

            await Tracked.Values
                .ToArray()
                .WhenAllAsync(async (tracked) =>
                {
                    var state = tracked.State;

                    try
                    {
                        await _eventstore.WriteEvents<TEntity>(tracked.Bucket, tracked.Id, tracked.Parents, tracked.Uncommitted, commitHeaders, tracked.Version - 1).ConfigureAwait(false);

                        if (tracked.Dirty && state.ShouldSnapshot())
                        {
                            var snapshot = state;
                            snapshot = (tracked as IEntity<TState>).SnapshotTaken(snapshot);
                            await _snapstore.WriteSnapshots<TEntity>(tracked.Bucket, tracked.Id, tracked.Parents, tracked.Version,
                                    snapshot, commitHeaders).ConfigureAwait(false);
                        }
                    }
                    catch (VersionException e)
                    {
                        Logger.Write(LogLevel.Info,
                            () => $"Stream [{tracked.Id}] entity {tracked.GetType().FullName} stream version {state.Version} commit verison {tracked.Version} has version conflicts with store - Message: {e.Message} Store: {e.InnerException?.Message}");

                        _metrics.Mark("Conflicts", Unit.Items);
                        // If we expected no stream, no reason to try to resolve the conflict
                        if (tracked.Version == 0)
                        {
                            Logger.Warn(
                                $"New stream [{tracked.Id}] entity {tracked.GetType().FullName} already exists in store");
                            throw new ConflictResolutionFailedException(
                                $"New stream [{tracked.Id}] entity {tracked.GetType().FullName} already exists in store");
                        }

                        try
                        {
                            var uncommitted = tracked.Uncommitted;
                            // make new clean entity
                            var clean = await GetUntracked(tracked.Bucket, tracked.Id, tracked.Parents).ConfigureAwait(false);

                            Logger.Write(LogLevel.Debug,
                                    () => $"Attempting to resolve conflict with strategy {_conflictResolution.Conflict}");
                            var strategy = _conflictResolution.Conflict.Build(Configuration.Settings.Container, _conflictResolution.Resolver);
                            await strategy.Resolve<TEntity, TState>(clean, uncommitted, commitId,
                                        commitHeaders).ConfigureAwait(false);

                            Logger.WriteFormat(LogLevel.Info,
                                "Stream [{0}] entity {1} version {2} had version conflicts with store - successfully resolved",
                                tracked.Id, tracked.GetType().FullName, state.Version);
                        }
                        catch (AbandonConflictException abandon)
                        {
                            _metrics.Mark("Conflicts Unresolved", Unit.Items);
                            Logger.WriteFormat(LogLevel.Error,
                                "Stream [{0}] entity {1} has version conflicts with store - abandoning resolution",
                                tracked.Id, tracked.GetType().FullName);
                            throw new ConflictResolutionFailedException(
                                $"Aborted conflict resolution for stream [{tracked.Id}] entity {tracked.GetType().FullName}",
                                abandon);
                        }
                        catch (Exception ex)
                        {
                            _metrics.Mark("Conflicts Unresolved", Unit.Items);
                            Logger.WriteFormat(LogLevel.Error,
                                "Stream [{0}] entity {1} has version conflicts with store - FAILED to resolve due to: {3}: {2}",
                                tracked.Id, tracked.GetType().FullName, ex.Message, ex.GetType().Name);
                            throw new ConflictResolutionFailedException(
                                $"Failed to resolve conflict for stream [{tracked.Id}] entity {tracked.GetType().FullName} due to exception",
                                ex);
                        }

                    }
                    catch (PersistenceException e)
                    {
                        Logger.WriteFormat(LogLevel.Warn,
                            "Failed to commit events to store for stream: [{0}] bucket [{1}] Exception: {3}: {2}",
                            tracked.Id, tracked.Bucket, e.Message, e.GetType().Name);
                        _metrics.Mark("Event Write Errors", Unit.Items);
                        throw;
                    }
                    catch (DuplicateCommitException)
                    {
                        Logger.WriteFormat(LogLevel.Warn,
                            "Detected a double commit for stream: [{0}] bucket [{1}] - discarding changes for this stream",
                            tracked.Id, tracked.Bucket);
                        _metrics.Mark("Event Write Errors", Unit.Errors);
                        // I was throwing this, but if this happens it means the events for this message have already been committed.  Possibly as a partial message failure earlier. 
                        // Im changing to just discard the changes, perhaps can take a deeper look later if this ever bites me on the ass
                        //throw;
                    }
                });


            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(TEntity).FullName} finished commit {commitId}");
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

        public virtual Task<TEntity> TryGet(Id id)
        {
            return TryGet(Defaults.Bucket, id);
        }
        public async Task<TEntity> TryGet(string bucket, Id id)
        {
            if (id == null) return default(TEntity);

            try
            {
                return await Get(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return default(TEntity);
        }

        public virtual Task<TEntity> Get(Id id)
        {
            return Get(Defaults.Bucket, id);
        }

        public async Task<TEntity> Get(string bucket, Id id)
        {
            var cacheId = $"{bucket}.{id}";
            TEntity root;
            if (!Tracked.TryGetValue(cacheId, out root))
                Tracked[cacheId] = root = await GetUntracked(bucket, id).ConfigureAwait(false);

            return root;
        }
        protected virtual async Task<TEntity> GetUntracked(string bucket, Id id, Id[] parents = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving entity id [{id}] bucket [{bucket}] for type {typeof(TEntity).FullName} in store");

            // Todo: pass parent instead of Id[]?
            var snapshot = await _snapstore.GetSnapshot<TEntity>(bucket, id, parents).ConfigureAwait(false);
            var events = await _eventstore.GetEvents<TEntity>(bucket, id, parents, start: snapshot?.Version).ConfigureAwait(false);

            var entity = Factory.Create(bucket, id, parents, events, snapshot?.Payload);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;

            Logger.Write(LogLevel.Debug, () => $"Hydrated entity id [{id}] in bucket [{bucket}] for type {typeof(TEntity).FullName} to version {entity.Version}");
            return entity;
        }

        public virtual Task<TEntity> New(Id id)
        {
            return New(Defaults.Bucket, id);
        }

        public async Task<TEntity> New(string bucket, Id id)
        {
            TEntity root;
            var cacheId = $"{bucket}.{id}";
            if (!Tracked.TryGetValue(cacheId, out root))
                Tracked[cacheId] = root = await NewUntracked(bucket, id).ConfigureAwait(false);
            return root;
        }
        protected virtual Task<TEntity> NewUntracked(string bucket, Id id, Id[] parents = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Creating new stream id [{id}] in bucket [{bucket}] for type {typeof(TEntity).FullName} in store");

            var entity = Factory.Create(bucket, id, parents);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;

            return Task.FromResult(entity);
        }

    }
}
