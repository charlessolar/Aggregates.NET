using System;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IWritableEvent
    {
        Guid? EventId { get; }
        object Event { get; }
        IEventDescriptor Descriptor { get; }
    }
}