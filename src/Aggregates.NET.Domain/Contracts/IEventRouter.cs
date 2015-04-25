using System;

namespace Aggregates.Contracts
{
    public interface IEventRouter
    {
        void RouteFor(Type @eventType, object @event);
    }
}