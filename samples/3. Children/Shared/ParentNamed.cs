using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("ParentNamed", "Samples")]
    public interface ParentNamed : Aggregates.Messages.IEvent
    {
        DateTime Timestamp { get; set; }
        string Name { get; set; }
    }
}
