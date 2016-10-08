using NServiceBus.ObjectBuilder;

namespace Aggregates.Contracts
{
    public interface INeedBuilder
    {
        IBuilder Builder { get; set; }
    }
}
