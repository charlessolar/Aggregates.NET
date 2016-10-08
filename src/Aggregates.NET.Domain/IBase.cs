using Aggregates.Contracts;

namespace Aggregates
{
    public interface IBase<TId> : IEventSource<TId>, IQueryResponse
    {
    }
}
