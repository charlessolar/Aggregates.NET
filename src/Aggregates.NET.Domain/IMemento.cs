using System;
namespace Aggregates
{
    public interface IMemento<TId>
    {
        TId EntityId { get; }
    }
}
