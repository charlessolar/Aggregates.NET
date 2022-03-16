using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("Echo", "Samples")]
    public interface Echo : IEvent
    {
        DateTime Timestamp { get; set; }
        string Message { get; set; }
    }
}
