using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("ParentNamed", "Samples")]
    public interface ParentNamed : IEvent
    {
        DateTime Timestamp { get; set; }
        string Name { get; set; }
    }
}
