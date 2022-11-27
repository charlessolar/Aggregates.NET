using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("Send", "Samples")]
    public class Send : Aggregates.Messages.ICommand
    {
        public string Message { get; set; } = default!;
    }
}
