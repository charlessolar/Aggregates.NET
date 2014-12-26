using System;
namespace Aggregates.Contracts
{
    public interface IMemento<TId>
    {
        TId Id { get; set; }
    }
}
