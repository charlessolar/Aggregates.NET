using System;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IFullEvent
    {
        Guid? EventId { get; }
        object Event { get; }
        IEventDescriptor Descriptor { get; }
    }
}