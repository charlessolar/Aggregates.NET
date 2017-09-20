using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Messages;

namespace Aggregates.Internal
{
    static class EntityFactory
    {
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

    class EntityFactory<TEntity, TState> : IEntityFactory<TEntity> where TEntity : Entity<TEntity, TState> where TState : IState, new()
    {
        readonly Func<TEntity> _factory;

        public EntityFactory()
        {
            _factory = ReflectionExtensions.BuildCreateEntityFunc<TEntity>();
        }

        public TEntity Create(string bucket, Id id, Id[] parents = null, IFullEvent[] events = null, IState snapshot = null)
        {
            // Todo: Can use a simple duck type helper incase snapshot type != TState due to refactor or something
            if (snapshot != null && !(snapshot is TState))
                throw new ArgumentException(
                    $"Snapshot type {snapshot.GetType().Name} doesn't match {typeof(TState).Name}");

            var state = snapshot ?? new TState();
            state.Id = id;
            state.Bucket = bucket;

            state.Parents = parents;
            state.Version = state.Version;
            state.Snapshot = (TState)snapshot;

            if (events != null)
                for (var i = 0; i < events.Length; i++)
                    state.Apply(events[i].Event as IEvent);

            var entity = _factory();
            (entity as IEntity<TState>).Instantiate((TState) state);

            
            return entity;
        }
        
    }
}
