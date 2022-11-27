using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("NameChild", "Samples")]
    public class NameChild : Aggregates.Messages.ICommand
    {
        public string ParentName { get; set; } = default!;
        public string Name { get; set; } = default!;
    }
}
