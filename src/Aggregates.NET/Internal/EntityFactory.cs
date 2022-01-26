using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Messages;
using Microsoft.Extensions.Logging;

namespace Aggregates.Internal
{
    static class EntityFactory
    {
        public static readonly long NewEntityVersion = -1;
        private static readonly ConcurrentDictionary<Type, object> Factories = new ConcurrentDictionary<Type, object>();

        public static IEntityFactory<TEntity> For<TEntity>() where TEntity : IEntity
        {
            var factory = Factories.GetOrAdd(typeof(TEntity), t => CreateFactory<TEntity>());

            return factory as IEntityFactory<TEntity>;
        }

        private static IEntityFactory<TEntity> CreateFactory<TEntity>()
            where TEntity : IEntity
        {
            var stateType = typeof(TEntity).BaseType.GetGenericArguments()[1];
            var factoryType = typeof(EntityFactory<,>).MakeGenericType(typeof(TEntity), stateType);

            return Activator.CreateInstance(factoryType) as IEntityFactory<TEntity>;
        }
    }

    public class EntityFactory<TEntity, TState> : IEntityFactory<TEntity> where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private readonly Func<TEntity> _factory;

        public EntityFactory()
        {
            _factory = ReflectionExtensions.BuildCreateEntityFunc<TEntity>();
        }

        public TEntity Create(ILogger Logger, string bucket, Id id, IParentDescriptor[] parents = null, IEvent[] events = null, object snapshot = null)
        {
            // Todo: Can use a simple duck type helper incase snapshot type != TState due to refactor or something
            if (snapshot != null && !(snapshot is TState))
                throw new ArgumentException(
                    $"Snapshot type {snapshot.GetType().Name} doesn't match {typeof(TState).Name}");

            var snapshotState = snapshot as TState;

            var state = snapshotState ?? new TState() { Version = EntityFactory.NewEntityVersion };
            state.Logger = Logger;

            state.Id = id;
            state.Bucket = bucket;

            state.Parents = parents;
            // this is set from SnapshotReader.cs L:164
            //state.Snapshot = snapshotState?.Copy();

            if (snapshotState != null)
            {
                Logger.DebugEvent("SnapshotRestored", "[{Stream:l}] bucket [{Bucket:l}] entity [{EntityType:l}] version {Version}", id, bucket, typeof(TEntity).Name, state.Version);
                state.SnapshotRestored();
            }

            if (events != null && events.Length > 0)
            {
                for (var i = 0; i < events.Length; i++)
                        state.Apply(events[i]);
            }

            var entity = _factory();
            (entity as IEntity<TState>).Instantiate(state);

            return entity;
        }

    }
}
