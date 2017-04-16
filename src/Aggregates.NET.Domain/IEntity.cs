using Aggregates.Contracts;

namespace Aggregates
{
    public interface IEntity : IEventSource, IQueryResponse
    {
    }
    public interface IEntity<out TParent> : IEntity where TParent : class, IEntity
    {
        new TParent Parent { get; }
    }
}
