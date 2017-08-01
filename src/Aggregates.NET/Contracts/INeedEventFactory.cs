using NServiceBus;

namespace Aggregates.Contracts
{
    public interface INeedEventFactory
    {
        IMessageCreator EventFactory { get; set; }
    }
}
