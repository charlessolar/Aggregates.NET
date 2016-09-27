using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventMutator
    {
        IEvent MutateIncoming(IEvent @event, IReadOnlyDictionary<String, String> headers);
        IEvent MutateOutgoing(IEvent @event);
    }
}
