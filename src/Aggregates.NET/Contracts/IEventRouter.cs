using System;

namespace Aggregates.Contracts
{
    public interface IEventRouter
    {
        Action<Object> RouteFor(Type @eventType);
    }
}