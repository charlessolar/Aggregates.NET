using System;

namespace Aggregates.Contracts
{
    public interface IEventRouter
    {
        void Register(Type eventType, Action<Object> handler);
        void Register<TId>(Aggregate<TId> aggregate);

        Action<Object> Get(Type @eventType);
    }
}