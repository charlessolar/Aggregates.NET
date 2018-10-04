
namespace Aggregates.Messages
{
    [Versioned("Error", "Aggregates", 1)]
    public interface Error : IMessage
    {
        string Message { get; set; }
        string Trace { get; set; }
    }
}
