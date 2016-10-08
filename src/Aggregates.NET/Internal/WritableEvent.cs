using System;
using Aggregates.Contracts;
using NServiceBus;

namespace Aggregates.Internal
{
    public class WritableEvent : IWritableEvent
    {
        public IEventDescriptor Descriptor { get; set; }
        public IEvent Event { get; set; }
        public Guid? EventId { get; set; }
    }
}