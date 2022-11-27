using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("ChildNamed", "Samples")]
    public interface ChildNamed : Aggregates.Messages.IEvent
    {
        DateTime Timestamp { get; set; }
        string Parent { get; set; }
        string Name { get; set; }
    }
}
