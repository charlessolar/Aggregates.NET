using NServiceBus;

namespace Aggregates.Messages
{
    public interface IError : IMessage
    {
        string Message { get; set; }
    }
}
