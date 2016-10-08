using Aggregates.Exceptions;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface IReject : IMessage
    {
        BusinessException Exception { get; set; }
    
        string Message { get; set; }
    }
}