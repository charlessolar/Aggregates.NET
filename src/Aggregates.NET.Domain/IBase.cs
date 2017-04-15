using Aggregates.Contracts;

namespace Aggregates
{
    public interface IBase : IEventSource, IQueryResponse
    {
    }
}
