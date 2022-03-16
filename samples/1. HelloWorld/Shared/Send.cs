using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("Send", "Samples")]
    public class Send : ICommand
    {
        public string Message { get; set; } = default!;
    }
}
