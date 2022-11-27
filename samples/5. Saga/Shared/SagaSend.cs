using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("SagaSend", "Samples")]
    public class SagaSend : Aggregates.Messages.ICommand
    {
        public string Message { get; set; } = default!;
    }
}
