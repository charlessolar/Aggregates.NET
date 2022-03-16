using Aggregates;
using Aggregates.Messages;

namespace Shared
{
    [Versioned("SagaSend", "Samples")]
    public class SagaSend : ICommand
    {
        public string Message { get; set; } = default!;
    }
}
