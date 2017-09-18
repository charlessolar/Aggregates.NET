using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Contracts
{
    public interface IEventFactory
    {
        T Create<T>(Action<T> action);

        object Create(Type type);
    }
}
