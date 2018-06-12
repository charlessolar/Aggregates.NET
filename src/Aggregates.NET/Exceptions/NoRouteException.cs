using System;

namespace Aggregates.Exceptions
{
    public class NoRouteException : Exception
    {
        public NoRouteException(Type state, string handler) : base($"State {state.FullName} does not have handler for event {handler}") { }
    }
}
