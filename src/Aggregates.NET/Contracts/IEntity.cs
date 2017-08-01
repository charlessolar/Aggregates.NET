
namespace Aggregates.Contracts
{
    public interface IEntity : IEventSource, IQueryResponse
    {
    }
    public interface IEntity<TParent> : IEntity where TParent : class, IEntity
    {
    }
}
