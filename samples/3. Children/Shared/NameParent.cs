using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("NameParent", "Samples")]
    public class NameParent : ICommand
    {
        public string Name { get; set; } = default!;
    }
}
