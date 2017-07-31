using System;

namespace Aggregates.Contracts
{
    public interface IRouteResolver
    {
        Action<IEventSource, object> Resolve(IEventSource eventsource, Type eventType);
        Action<IEventSource, object> Conflict(IEventSource eventsource, Type eventType);
    }
}
