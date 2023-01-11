using System;

namespace Aggregates.Contracts
{
    public interface IEventFactory
    {
        T Create<T>(Action<T> action);

        object Create(Type type);
    }
}
