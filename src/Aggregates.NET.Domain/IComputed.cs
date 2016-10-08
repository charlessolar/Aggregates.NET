using NServiceBus;

namespace Aggregates
{
    public interface IComputed<TResponse> : IMessage
    {
    }
}
