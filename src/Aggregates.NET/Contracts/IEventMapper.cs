using System;

namespace Aggregates.Contracts
{
    public interface IEventMapper
    {
        void Initialize(Type type);
        Type GetMappedTypeFor(Type type);
    }
}
