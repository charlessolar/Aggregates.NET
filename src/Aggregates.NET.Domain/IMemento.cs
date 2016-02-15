using System;
namespace Aggregates
{
    public interface IMemento<TId>
    {
        TId Id { get; }
    }
}
