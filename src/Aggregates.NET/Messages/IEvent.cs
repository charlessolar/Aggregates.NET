

namespace Aggregates.Messages
{
    [Versioned("IEvent", "Aggregates", 1)]
    public interface IEvent : IMessage
    {
    }
}
