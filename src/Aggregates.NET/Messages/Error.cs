
namespace Aggregates.Messages
{
    [Versioned("Error", "Aggregates")]
    public interface Error : IMessage
    {
        string Message { get; set; }
        string Trace { get; set; }
    }
}
