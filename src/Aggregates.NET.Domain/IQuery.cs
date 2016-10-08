using NServiceBus;

namespace Aggregates
{
    public interface IQuery<TResponse> : IMessage where TResponse : IQueryResponse
    {
    }
}
