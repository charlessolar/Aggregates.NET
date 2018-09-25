using Aggregates.Exceptions;

namespace Aggregates.Messages
{
    [Versioned("Reject", "Aggregates")]
    public interface Reject : IMessage
    {
        BusinessException Exception { get; set; }
    
        string Message { get; set; }
    }
}