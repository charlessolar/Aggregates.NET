using System;
using System.Collections.Concurrent;
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
    class Repository<TEntity, TState, TParent> : Repository<TEntity, TState>, IRepository<TEntity, TParent> where TParent : IEntity where TEntity : Entity<TEntity, TState, TParent> where TState : class, IState, new()
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Repository");

        private readonly TParent _parent;

        public Repository(TParent parent, IMetrics metrics, IStoreEvents store, IStoreSnapshots snapshots, IOobWriter oobStore, IEventFactory factory, IDomainUnitOfWork uow)
            : base(metrics, store, snapshots, oobStore, factory, uow)
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
            {
                root = await GetUntracked(_parent.Bucket, id, _parent.BuildParents()).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }

        public override async Task<TEntity> New(Id id)
        {
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

        protected override async Task<TEntity> GetUntracked(string bucket, Id id, Id[] parents)
        {
            var entity = await base.GetUntracked(bucket, id, parents).ConfigureAwait(false);

            entity.Parent = _parent;

            return entity;
        }

        protected override async Task<TEntity> NewUntracked(string bucket, Id id, Id[] parents)
        {
            var entity = await base.NewUntracked(bucket, id, parents).ConfigureAwait(false);

            entity.Parent = _parent;

            return entity;
        }
    }
    class Repository<TEntity, TState> : IRepository<TEntity>, IRepository where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Repository");
        private static readonly IEntityFactory<TEntity> Factory = EntityFactory.For<TEntity>();

        private static OptimisticConcurrencyAttribute _conflictResolution;

        protected readonly ConcurrentDictionary<string, TEntity> Tracked = new ConcurrentDictionary<string, TEntity>();
        protected readonly IMetrics _metrics;
        private readonly IStoreEvents _eventstore;
        private readonly IStoreSnapshots _snapstore;
        private readonly IOobWriter _oobStore;
        private readonly IEventFactory _factory;
        protected readonly IDomainUnitOfWork _uow;

        private bool _disposed;

        public int ChangedStreams => Tracked.Count(x => x.Value.Dirty);

        // Todo: too many operations on this class, make a "EntityWriter" contract which does event, oob, and snapshot writing
        public Repository(IMetrics metrics, IStoreEvents store, IStoreSnapshots snapshots, IOobWriter oobStore, IEventFactory factory, IDomainUnitOfWork uow)
        {
            _metrics = metrics;
            _eventstore = store;
            _snapstore = snapshots;
            _oobStore = oobStore;
            _factory = factory;
            _uow = uow;

            // Conflict resolution is strong by default
            if (_conflictResolution == null)
                _conflictResolution = (OptimisticConcurrencyAttribute)Attribute.GetCustomAttribute(typeof(TEntity), typeof(OptimisticConcurrencyAttribute))
                                      ?? new OptimisticConcurrencyAttribute(ConcurrencyConflict.Throw);
        }
        Task IRepository.Prepare(Guid commitId)
        {
            Logger.DebugEvent("Prepare", "{EntityType} prepare {CommitId}", typeof(TEntity).FullName, commitId);
            // Verify streams we read but didn't change are still save version
            return
                Tracked.Values
                    .Where(x => !x.Dirty)
                    .ToArray()
                    .WhenAllAsync((x) => _eventstore.VerifyVersion<TEntity>(x.Bucket, x.Id, x.Parents, x.Version));
        }


        async Task IRepository.Commit(Guid commitId, IDictionary<string, string> commitHeaders)
        {
            Logger.DebugEvent("Commit", "[{EntityType:l}] commit {CommitId}", typeof(TEntity).FullName, commitId);

            await Tracked.Values
                .ToArray()
                .Where(x => x.Dirty)
                .WhenAllAsync(async (tracked) =>
                {
                    var state = tracked.State;

                    var domainEvents = tracked.Uncommitted.Where(x => x.Descriptor.StreamType == StreamTypes.Domain).ToArray();
                    var oobEvents = tracked.Uncommitted.Where(x => x.Descriptor.StreamType == StreamTypes.OOB).ToArray();

                    try
                    {
                        if (domainEvents.Any())
                        {
                            await _eventstore.WriteEvents<TEntity>(tracked.Bucket, tracked.Id, tracked.Parents,
                                domainEvents, commitHeaders, tracked.Version).ConfigureAwait(false);
                        }
                    }
                    catch (VersionException e)
                    {
                        Logger.DebugEvent("VersionConflict", "[{EntityId:l}] entity [{EntityType:l}] version {Version} commit version {CommitVersion} - {StoreMessage}", tracked.Id, typeof(TEntity).FullName, state.Version, tracked.Version, e.Message);
                        _metrics.Mark("Conflicts", Unit.Items);
                        // If we expected no stream, no reason to try to resolve the conflict
                        if (tracked.Version == EntityFactory.NewEntityVersion)
                        {
                            Logger.DebugEvent("AlreadyExists", "[{EntityId:l}] entity [{EntityType:l}] already exists", tracked.Id, typeof(TEntity).FullName);
                            throw new ConflictResolutionFailedException(
                                $"New stream [{tracked.Id}] entity {tracked.GetType().FullName} already exists in store");
                        }

                        try
                        {
                            // make new clean entity
                            var clean = await GetClean(tracked).ConfigureAwait(false);

                            Logger.DebugEvent("ConflictResolve", "[{EntityId:l}] entity [{EntityType:l}] resolving {ConflictingEvents} events with {ConflictResolver}", tracked.Id, typeof(TEntity).FullName, state.Version - clean.Version, _conflictResolution.Conflict);
                            var strategy = _conflictResolution.Conflict.Build(Configuration.Settings.Container, _conflictResolution.Resolver);

                            commitHeaders[Defaults.ConflictResolvedHeader] = _conflictResolution.Conflict.DisplayName;

                            await strategy.Resolve<TEntity, TState>(clean, domainEvents, commitId, commitHeaders).ConfigureAwait(false);
                            // Conflict resolved, replace original dirty entity we were trying to save with clean one
                            tracked = clean;

                            Logger.DebugEvent("ConflictResolveSuccess", "[{EntityId:l}] entity [{EntityType:l}] resolution success", tracked.Id, typeof(TEntity).FullName);
                        }
                        catch (AbandonConflictException abandon)
                        {
                            _metrics.Mark("Conflicts Unresolved", Unit.Items);
                            Logger.ErrorEvent("ConflictResolveAbandon", "[{EntityId:l}] entity [{EntityType:l}] abandonded", tracked.Id, typeof(TEntity).FullName);

                            throw new ConflictResolutionFailedException(
                                $"Aborted conflict resolution for stream [{tracked.Id}] entity {tracked.GetType().FullName}",
                                abandon);
                        }
                        catch (Exception ex)
                        {
                            _metrics.Mark("Conflicts Unresolved", Unit.Items);
                            Logger.ErrorEvent("ConflictResolveFail", ex, "[{EntityId:l}] entity [{EntityType:l}] failed: {ExceptionType} - {ExceptionMessage}", tracked.Id, typeof(TEntity).FullName, ex.GetType().Name, ex.Message);

                            throw new ConflictResolutionFailedException(
                                $"Failed to resolve conflict for stream [{tracked.Id}] entity {tracked.GetType().FullName} due to exception",
                                ex);
                        }

                    }
                    catch (PersistenceException e)
                    {
                        Logger.WarnEvent("CommitFailure", e, "[{EntityId:l}] entity [{EntityType:l}] bucket [{Bucket:l}]: {ExceptionType} - {ExceptionMessage}", tracked.Id, typeof(TEntity).Name, tracked.Bucket, e.GetType().Name, e.Message);
                        _metrics.Mark("Event Write Errors", Unit.Errors);
                        throw;
                    }
                    catch (DuplicateCommitException)
                    {
                        Logger.Warn("DoubleCommit", "[{EntityId:l}] entity [{EntityType:l}]", tracked.Id, typeof(TEntity).FullName);

                        _metrics.Mark("Event Write Errors", Unit.Errors);
                        // I was throwing this, but if this happens it means the events for this message have already been committed.  Possibly as a partial message failure earlier. 
                        // Im changing to just discard the changes, perhaps can take a deeper look later if this ever bites me on the ass
                        //throw;
                    }

                    try
                    {
                        if (oobEvents.Any())
                            await _oobStore.WriteEvents<TEntity>(tracked.Bucket, tracked.Id, tracked.Parents, oobEvents, commitId, commitHeaders).ConfigureAwait(false);

                        if (tracked.State.ShouldSnapshot())
                        {
                            // Notify the entity and state that we are taking a snapshot
                            (tracked as IEntity<TState>).Snapshotting();
                            tracked.State.Snapshotting();
                            await _snapstore.WriteSnapshots<TEntity>(tracked.State, commitHeaders).ConfigureAwait(false);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.WarnEvent("SecondaryFailure", "[{EntityId:l}] entity [{EntityType:l}] bucket [{Bucket:l}]: {ExceptionType} - {ExceptionMessage}", tracked.Id, typeof(TEntity).Name, tracked.Bucket, e.GetType().Name, e.Message);
                    }
                }).ConfigureAwait(false);


            Logger.DebugEvent("FinishedCommit", "[{EntityType:l}] commit {CommitId}", typeof(TEntity).FullName, commitId);
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
            {
                root = await GetUntracked(bucket, id).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }

            return root;
        }
        protected virtual async Task<TEntity> GetUntracked(string bucket, Id id, Id[] parents = null)
        {
            // Todo: pass parent instead of Id[]?
            var snapshot = await _snapstore.GetSnapshot<TEntity>(bucket, id, parents).ConfigureAwait(false);
            var events = await _eventstore.GetEvents<TEntity>(bucket, id, parents, start: snapshot?.Version).ConfigureAwait(false);

            var entity = Factory.Create(bucket, id, parents, events.Select(x => x.Event as IEvent).ToArray(), snapshot?.Payload);


            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobStore;

            Logger.DebugEvent("Get", "[{EntityId:l}] bucket [{Bucket:l}] entity [{EntityType:l}] version {Version}", id, bucket, typeof(TEntity).FullName, entity.Version);

            return entity;
        }

        private async Task<TEntity> GetClean(TEntity dirty)
        {
            // pull a new snapshot so snapshot.Snapshot is not null
            // if we just use dity.State.Snapshot then dirty.State.Snapshot.Snapshot will be null which will cause issues
            var snapshot = await _snapstore.GetSnapshot<TEntity>(dirty.Bucket, dirty.Id, dirty.Parents).ConfigureAwait(false);
            var events = dirty.State.Committed;

            var entity = Factory.Create(dirty.Bucket, dirty.Id, dirty.Parents, events, snapshot?.Payload);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobStore;

            Logger.DebugEvent("GetClean", "[{EntityId:l}] bucket [{Bucket:l}] entity [{EntityType:l}] version {Version}", dirty.Id, dirty.Bucket, typeof(TEntity).FullName, entity.Version);
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
            {
                root = await NewUntracked(bucket, id).ConfigureAwait(false);
                if (!Tracked.TryAdd(cacheId, root))
                    throw new InvalidOperationException($"Could not add cache key [{cacheId}] to repo tracked");
            }
            return root;
        }
        protected virtual Task<TEntity> NewUntracked(string bucket, Id id, Id[] parents = null)
        {
            Logger.DebugEvent("Create", "[{EntityId:l}] bucket [{Bucket:l}] entity [{EntityType:l}]", id, bucket, typeof(TEntity).FullName);

            var entity = Factory.Create(bucket, id, parents);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobStore;

            return Task.FromResult(entity);
        }

    }
}
