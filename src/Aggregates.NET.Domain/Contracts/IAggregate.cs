namespace Aggregates.Contracts
{
    public interface IAggregate : IEventSource
    {
    }

    public interface IAggregate<TId> : IAggregate, IBase<TId>
    {
    }
}