using Aggregates.Exceptions;

namespace Aggregates.Messages
{
    [Versioned("Reject", "Aggregates", 1)]
    public interface Reject : IMessage
    {
        BusinessException Exception { get; set; }
    
        string Message { get; set; }
    }
}