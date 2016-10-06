using Aggregates.Contracts;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class WritableEvent : IWritableEvent
    {
        public IEventDescriptor Descriptor { get; set; }
        public IEvent Event { get; set; }
        public Guid? EventId { get; set; }
    }
}