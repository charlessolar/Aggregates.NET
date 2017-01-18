using System;
using Aggregates.Contracts;
using NServiceBus;

namespace Aggregates.Internal
{
    class WritableEvent : IWritableEvent
    {
        public IEventDescriptor Descriptor { get; set; }
        public object Event { get; set; }
        public Guid? EventId { get; set; }
    
    }
}