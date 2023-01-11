using System;

namespace Aggregates.Contracts
{
    public interface ITimeProvider
    {
        DateTime Now { get; }
    }
}
