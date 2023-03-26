using Aggregates.Messages;
using System;

namespace Aggregates.Contracts
{
    public interface IFullEvent {
		Guid? EventId { get; }
        IEvent Event { get; }
		string EventType { get; }
		IEventDescriptor Descriptor { get; }
    }
}