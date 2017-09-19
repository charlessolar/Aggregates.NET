
namespace Aggregates.Messages
{
    public interface Error : IMessage
    {
        string Message { get; set; }
        string Trace { get; set; }
    }
}
