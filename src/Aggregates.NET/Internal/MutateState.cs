using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Messages;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

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

        public StateMutator()
        {
            _mutators = ReflectionExtensions.GetStateMutators<TState>();
        }

        public void Handle(IState state, IEvent @event)
        {
            if (@event == null)
                throw new ArgumentNullException(nameof(@event));

            // Todo: cheap hack NSB creates events as IEvent__impl
            // remove the __impl if it exists
            // (message mapper not working here in due to static creation)
            var eventType = @event.GetType().Name;
            if (eventType.EndsWith("impl"))
                eventType = eventType.Substring(0, eventType.Length - 6);

            // Todo: can suport "named" events with an attribute here so instead of routing based on object type 
            // route based on event name.
            if (!_mutators.TryGetValue($"Handle.{eventType}", out var eventMutator))
                throw new NoRouteException(typeof(TState), $"Handle({eventType})");
            eventMutator((TState)state, @event);
        }
    }
}
