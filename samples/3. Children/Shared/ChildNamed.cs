using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("ChildNamed", "Samples")]
    public interface ChildNamed : IEvent
    {
        DateTime Timestamp { get; set; }
        string Parent { get; set; }
        string Name { get; set; }
    }
}
