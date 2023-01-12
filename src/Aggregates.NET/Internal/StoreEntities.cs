using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class StoreEntities : IStoreEntities
    {
        private readonly ILogger Logger;

        private readonly ISettings _settings;
        private readonly IServiceProvider _provider;
        private readonly IMetrics _metrics;
        private readonly IStoreEvents _eventstore;
        private readonly IStoreSnapshots _snapstore;
        private readonly IEventFactory _factory;
        private readonly IVersionRegistrar _registrar;
        private readonly ITrackChildren _childTracker;

        public StoreEntities(
            ILogger<StoreEntities> logger,
            ISettings settings,
            IServiceProvider provider,
            IMetrics metrics,
            IStoreEvents eventstore,
            IStoreSnapshots snapstore,
            IEventFactory factory,
            IVersionRegistrar registrar,
            ITrackChildren childTracker
            )
        {
            Logger = logger;
            _settings = settings;
            _provider = provider;
            _metrics = metrics;
            _eventstore = eventstore;
            _snapstore = snapstore;
            _factory = factory;
            _registrar = registrar;
            _childTracker = childTracker;
        }

        private IParentDescriptor[] getParents(IEntity entity)
        {
            if (entity == null)
                return null;

            var parents = getParents((entity as IChildEntity)?.Parent)?.ToList() ?? new List<IParentDescriptor>();
            parents.Add(new ParentDescriptor { EntityType = _registrar.GetVersionedName(entity.GetType()), StreamId = entity.Id });
            return parents.ToArray();
        }
        public Task<TEntity> New<TEntity, TState>(string bucket, Id id, IEntity parent) where TEntity : IEntity<TState> where TState : class, IState, new()
        {
            var uow = _provider.GetRequiredService<Aggregates.UnitOfWork.IDomainUnitOfWork>();

            var factory = EntityFactory.For<TEntity>();

            Logger.DebugEvent("Create", "[{EntityId:l}] bucket [{Bucket:l}] entity [{EntityType:l}]", id, bucket, typeof(TEntity).FullName);

            var entity = factory.Create(Logger, bucket, id, getParents(parent));

            (entity as INeedDomainUow).Uow = uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedVersionRegistrar).Registrar = _registrar;
            (entity as INeedChildTracking).Tracker = _childTracker;

            return Task.FromResult(entity);
        }
        public async Task<TEntity> Get<TEntity, TState>(string bucket, Id id, IEntity parent) where TEntity : IEntity<TState> where TState : class, IState, new()
        {
            var uow = _provider.GetRequiredService<Aggregates.UnitOfWork.IDomainUnitOfWork>();

            var factory = EntityFactory.For<TEntity>();

            var parents = getParents(parent);
            // Todo: pass parent instead of Id[]?
            var snapshot = await _snapstore.GetSnapshot<TEntity, TState>(bucket, id, parents?.Select(x => x.StreamId).ToArray()).ConfigureAwait(false);
            var events = await _eventstore.GetEvents<TEntity>(StreamDirection.Forwards, bucket, id, parents?.Select(x => x.StreamId).ToArray(), start: snapshot?.Version).ConfigureAwait(false);

            var entity = factory.Create(Logger, bucket, id, parents, events.Select(x => x.Event).ToArray(), snapshot?.Payload);

            (entity as INeedDomainUow).Uow = uow;
            (entity as INeedEventFactory).EventFactory = _factory;
            (entity as INeedStore).Store = _eventstore;
            (entity as INeedVersionRegistrar).Registrar = _registrar;
            (entity as INeedChildTracking).Tracker = _childTracker;

            Logger.DebugEvent("Get", "[{EntityId:l}] bucket [{Bucket:l}] entity [{EntityType:l}] version {Version}", id, bucket, typeof(TEntity).FullName, entity.Version);

            return entity;
        }
        public Task Verify<TEntity, TState>(TEntity entity) where TEntity : IEntity<TState> where TState : class, IState, new()
        {
            if (entity.Dirty)
                throw new ArgumentException($"Cannot verify version for a dirty entity");

            return _eventstore.VerifyVersion<TEntity>(entity.Bucket, entity.Id, entity.GetParentIds(), entity.Version);
        }
        public async Task Commit<TEntity, TState>(TEntity entity, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : class, IState, new()
        {
            if (!entity.Dirty)
                throw new ArgumentException($"Entity {typeof(TEntity).FullName} id {entity.Id} bucket {entity.Bucket} is not dirty");

            var state = entity.State;

            var domainEvents = entity.Uncommitted.Where(x => x.Descriptor.StreamType == StreamTypes.Domain).ToArray();

            try
            {
                if (domainEvents.Any())
                {
                    await _eventstore.WriteEvents<TEntity>(entity.Bucket, entity.Id, entity.GetParentIds(),
                        domainEvents, commitHeaders, entity.Version).ConfigureAwait(false);
                }
            }
            catch (VersionException e)
            {
                Logger.WarnEvent("VersionConflict", "[{EntityId:l}] entity [{EntityType:l}] version {Version} commit version {CommitVersion} - {StoreMessage}", entity.Id, typeof(TEntity).FullName, state.Version, entity.Version, e.Message);
                _metrics.Mark("Conflicts", Unit.Items);
                // If we expected no stream, no reason to try to resolve the conflict
                if (entity.Version == EntityFactory.NewEntityVersion)
                {
                    Logger.DebugEvent("AlreadyExists", "[{EntityId:l}] entity [{EntityType:l}] already exists", entity.Id, typeof(TEntity).FullName);
                    throw new EntityAlreadyExistsException(typeof(TEntity).FullName, entity.Bucket, entity.Id, entity.GetParentIds());
                }
                throw;
            }
            catch (PersistenceException e)
            {
                Logger.WarnEvent("CommitFailure", e, "[{EntityId:l}] entity [{EntityType:l}] bucket [{Bucket:l}]: {ExceptionType} - {ExceptionMessage}", entity.Id, typeof(TEntity).Name, entity.Bucket, e.GetType().Name, e.Message);
                _metrics.Mark("Event Write Errors", Unit.Errors);
                throw;
            }

            try
            {
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
