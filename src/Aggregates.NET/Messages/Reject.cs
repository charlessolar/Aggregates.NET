using Aggregates.Exceptions;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface Reject : IMessage
    {
        BusinessException Exception { get; set; }
    
        string Message { get; set; }
    }
}