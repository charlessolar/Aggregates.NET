using System;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IWritableEvent
    {
        Guid? EventId { get; set; }
        IEvent Event { get; set; }
        IEventDescriptor Descriptor { get; set; }
    }
}