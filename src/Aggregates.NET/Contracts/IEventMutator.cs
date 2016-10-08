using System.Collections.Generic;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventMutator
    {
        IEvent MutateIncoming(IEvent @event, IReadOnlyDictionary<string, string> headers);
        IEvent MutateOutgoing(IEvent @event);
    }
}
