using Aggregates.Contracts;
using Aggregates.Messages;
using System;

namespace Aggregates.Internal
{
    class FullEvent : IFullEvent {
		public Guid? EventId { get; set; }
        public IEvent Event { get; set; }
		public string EventType { get; set; }
		public IEventDescriptor Descriptor { get; set; }
	}
}
