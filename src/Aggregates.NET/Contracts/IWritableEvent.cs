using System;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IWritableEvent
    {
        Guid? EventId { get; set; }
        object Event { get; set; }
        IEventDescriptor Descriptor { get; set; }
    }
}