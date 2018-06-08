using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;
using Aggregates.Messages;

namespace Aggregates.Internal
{
    class FullEvent : IFullEvent
    {
        public IEventDescriptor Descriptor { get; set; }
        public IEvent Event { get; set; }
        public Guid? EventId { get; set; }
    }
}
