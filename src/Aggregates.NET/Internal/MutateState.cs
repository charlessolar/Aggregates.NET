using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Messages;
using Aggregates.Exceptions;
using System.Linq;

namespace Aggregates.Internal
{
    // original: https://github.com/mfelicio/NDomain/blob/d30322bc64105ad2e4c961600ae24831f675b0e9/source/NDomain/StateMutator.cs

    internal static class StateMutators
    {
        private static readonly ConcurrentDictionary<Type, IMutateState> Mutators;

        static StateMutators()
        {
            Mutators = new ConcurrentDictionary<Type, IMutateState>();
        }

        public static IMutateState For(Type stateType)
        {
            return Mutators.GetOrAdd(stateType, t => CreateMutator(stateType));
        }

        private static IMutateState CreateMutator(Type stateType)
        {
            var mutatorType = typeof(StateMutator<>).MakeGenericType(stateType);
            var mutator = Activator.CreateInstance(mutatorType);

            return mutator as IMutateState;
        }
    }

    internal class StateMutator<TState> : IMutateState
        where TState : IState
    {
        private readonly Dictionary<string, Action<TState, object>> _mutators;
        private IEventMapper _mapper;

        public StateMutator()
        {
            _mutators = ReflectionExtensions.GetStateMutators<TState>();

            // because this type is created from a static class its hard to get injection going right
            _mapper = Configuration.Settings.Container.Resolve<IEventMapper>();
        }

        public void Handle(IState state, IEvent @event)
        {
            var eventType = @event.GetType();
            if(!eventType.IsInterface)
                eventType = _mapper.GetMappedTypeFor(eventType);
            
            // Todo: can suport "named" events with an attribute here so instead of routing based on object type 
            // route based on event name.
            Action<TState, object> eventMutator;
            if(!_mutators.TryGetValue($"Handle.{eventType.Name}", out eventMutator))
                throw new NoRouteException($"State {typeof(TState).Name} does not have handler for event {eventType.Name}");
            eventMutator((TState)state, @event);
        }
        public void Conflict(IState state, IEvent @event)
        {
            var eventType = @event.GetType();
            if (!eventType.IsInterface)
                eventType = _mapper.GetMappedTypeFor(eventType);

            // Todo: the "conflict." and "handle." key prefixes are a hack
            // Todo: can suport "named" events with an attribute here so instead of routing based on object type 
            // route based on event name.
            Action<TState, object> eventMutator;
            if (!_mutators.TryGetValue($"Conflict.{eventType.Name}", out eventMutator))
                throw new NoRouteException($"State {typeof(TState).Name} does not have handler for event {eventType.Name}");
            eventMutator((TState)state, @event);
        }
    }
}
