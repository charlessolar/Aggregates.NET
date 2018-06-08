using Aggregates.Attributes;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class StoreEntities : IStoreEntities
    {
        private static readonly ILog Logger = LogProvider.GetLogger("StoreEntities");

        private readonly IMetrics _metrics;
        private readonly IStoreEvents _eventstore;
        private readonly IStoreSnapshots _snapstore;
        private readonly IOobWriter _oobstore;
        private readonly IEventFactory _factory;
        private readonly IDomainUnitOfWork _uow;

        public StoreEntities(IMetrics metrics, IStoreEvents eventstore, IStoreSnapshots snapstore, IOobWriter oobstore, IEventFactory factory, IDomainUnitOfWork uow)
        {
            _metrics = metrics;
            _eventstore = eventstore;
            _snapstore = snapstore;
            _oobstore = oobstore;
            _factory = factory;
            _uow = uow;

        }

        public Task<TEntity> New<TEntity, TState>(string bucket, Id id, Id[] parents) where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
        {
            var factory = EntityFactory.For<TEntity>();

            Logger.DebugEvent("Create", "[{EntityId:l}] bucket [{Bucket:l}] entity [{EntityType:l}]", id, bucket, typeof(TEntity).FullName);

            var entity = factory.Create(bucket, id, parents);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobstore;

            return Task.FromResult(entity);
        }
        public async Task<TEntity> Get<TEntity, TState>(string bucket, Id id, Id[] parents) where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
        {
            var factory = EntityFactory.For<TEntity>();

            // Todo: pass parent instead of Id[]?
            var snapshot = await _snapstore.GetSnapshot<TEntity>(bucket, id, parents).ConfigureAwait(false);
            var events = await _eventstore.GetEvents<TEntity>(bucket, id, parents, start: snapshot?.Version).ConfigureAwait(false);

            var entity = factory.Create(bucket, id, parents, events.Select(x => x.Event as IEvent).ToArray(), snapshot?.Payload);


            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobstore;

            Logger.DebugEvent("Get", "[{EntityId:l}] bucket [{Bucket:l}] entity [{EntityType:l}] version {Version}", id, bucket, typeof(TEntity).FullName, entity.Version);

            return entity;
        }
        private async Task<TEntity> GetClean<TEntity, TState>(TEntity dirty) where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
        {
            var factory = EntityFactory.For<TEntity>();
            // pull a new snapshot so snapshot.Snapshot is not null
            // if we just use dity.State.Snapshot then dirty.State.Snapshot.Snapshot will be null which will cause issues
            var snapshot = await _snapstore.GetSnapshot<TEntity>(dirty.Bucket, dirty.Id, dirty.Parents).ConfigureAwait(false);
            var events = dirty.State.Committed;

            var entity = factory.Create(dirty.Bucket, dirty.Id, dirty.Parents, events, snapshot?.Payload);

            (entity as INeedDomainUow).Uow = _uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedStore).OobWriter = _oobstore;

            Logger.DebugEvent("GetClean", "[{EntityId:l}] bucket [{Bucket:l}] entity [{EntityType:l}] version {Version}", dirty.Id, dirty.Bucket, typeof(TEntity).FullName, entity.Version);
            return entity;
        }
        public Task Verify<TEntity, TState>(string bucket, Id id, Id[] parents, long version) where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
        {
            return _eventstore.VerifyVersion<TEntity>(bucket, id, parents, version);
        }
        public async Task Commit<TEntity, TState>(TEntity entity, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
        {

            var state = entity.State;

            var domainEvents = entity.Uncommitted.Where(x => x.Descriptor.StreamType == StreamTypes.Domain).ToArray();
            var oobEvents = entity.Uncommitted.Where(x => x.Descriptor.StreamType == StreamTypes.OOB).ToArray();

            try
            {
                if (domainEvents.Any())
                {
                    await _eventstore.WriteEvents<TEntity>(entity.Bucket, entity.Id, entity.Parents,
                        domainEvents, commitHeaders, entity.Version).ConfigureAwait(false);
                }
            }
            catch (VersionException e)
            {
                Logger.DebugEvent("VersionConflict", "[{EntityId:l}] entity [{EntityType:l}] version {Version} commit version {CommitVersion} - {StoreMessage}", entity.Id, typeof(TEntity).FullName, state.Version, entity.Version, e.Message);
                _metrics.Mark("Conflicts", Unit.Items);
                // If we expected no stream, no reason to try to resolve the conflict
                if (entity.Version == EntityFactory.NewEntityVersion)
                {
                    Logger.DebugEvent("AlreadyExists", "[{EntityId:l}] entity [{EntityType:l}] already exists", entity.Id, typeof(TEntity).FullName);
                    throw new EntityAlreadyExistsException<TEntity>(entity.Bucket, entity.Id, entity.Parents);
                }

                try
                {
                    // Todo: cache per entity type
                    var conflictResolution = (OptimisticConcurrencyAttribute)Attribute.GetCustomAttribute(typeof(TEntity), typeof(OptimisticConcurrencyAttribute))
                                          ?? new OptimisticConcurrencyAttribute(ConcurrencyConflict.Throw);

                    // make new clean entity
                    var clean = await GetClean<TEntity, TState>(entity).ConfigureAwait(false);

                    Logger.DebugEvent("ConflictResolve", "[{EntityId:l}] entity [{EntityType:l}] resolving {ConflictingEvents} events with {ConflictResolver}", entity.Id, typeof(TEntity).FullName, state.Version - clean.Version, conflictResolution.Conflict);
                    var strategy = conflictResolution.Conflict.Build(conflictResolution.Resolver);

                    commitHeaders[Defaults.ConflictResolvedHeader] = conflictResolution.Conflict.DisplayName;

                    await strategy.Resolve<TEntity, TState>(clean, domainEvents, commitId, commitHeaders).ConfigureAwait(false);
                    // Conflict resolved, replace original dirty entity we were trying to save with clean one
                    entity = clean;

                    Logger.DebugEvent("ConflictResolveSuccess", "[{EntityId:l}] entity [{EntityType:l}] resolution success", entity.Id, typeof(TEntity).FullName);
                }
                catch (AbandonConflictException abandon)
                {
                    _metrics.Mark("Conflicts Unresolved", Unit.Items);
                    Logger.ErrorEvent("ConflictResolveAbandon", "[{EntityId:l}] entity [{EntityType:l}] abandonded", entity.Id, typeof(TEntity).FullName);

                    throw new ConflictResolutionFailedException(
                        $"Aborted conflict resolution for stream [{entity.Id}] entity {entity.GetType().FullName}",
                        abandon);
                }
                catch (Exception ex)
                {
                    _metrics.Mark("Conflicts Unresolved", Unit.Items);
                    Logger.ErrorEvent("ConflictResolveFail", ex, "[{EntityId:l}] entity [{EntityType:l}] failed: {ExceptionType} - {ExceptionMessage}", entity.Id, typeof(TEntity).FullName, ex.GetType().Name, ex.Message);

                    throw new ConflictResolutionFailedException(
                        $"Failed to resolve conflict for stream [{entity.Id}] entity {entity.GetType().FullName} due to exception",
                        ex);
                }

            }
            catch (PersistenceException e)
            {
                Logger.WarnEvent("CommitFailure", e, "[{EntityId:l}] entity [{EntityType:l}] bucket [{Bucket:l}]: {ExceptionType} - {ExceptionMessage}", entity.Id, typeof(TEntity).Name, entity.Bucket, e.GetType().Name, e.Message);
                _metrics.Mark("Event Write Errors", Unit.Errors);
                throw;
            }
            catch (DuplicateCommitException)
            {
                Logger.Warn("DoubleCommit", "[{EntityId:l}] entity [{EntityType:l}]", entity.Id, typeof(TEntity).FullName);

                _metrics.Mark("Event Write Errors", Unit.Errors);
                // I was throwing this, but if this happens it means the events for this message have already been committed.  Possibly as a partial message failure earlier. 
                // Im changing to just discard the changes, perhaps can take a deeper look later if this ever bites me on the ass
                //throw;
            }

            try
            {
                if (oobEvents.Any())
                    await _oobstore.WriteEvents<TEntity>(entity.Bucket, entity.Id, entity.Parents, oobEvents, commitId, commitHeaders).ConfigureAwait(false);

                if (entity.State.ShouldSnapshot())
                {
                    // Notify the entity and state that we are taking a snapshot
                    (entity as IEntity<TState>).Snapshotting();
                    entity.State.Snapshotting();
                    await _snapstore.WriteSnapshots<TEntity>(entity.State, commitHeaders).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Logger.WarnEvent("SecondaryFailure", "[{EntityId:l}] entity [{EntityType:l}] bucket [{Bucket:l}]: {ExceptionType} - {ExceptionMessage}", entity.Id, typeof(TEntity).Name, entity.Bucket, e.GetType().Name, e.Message);
            }
        }
    }
}
