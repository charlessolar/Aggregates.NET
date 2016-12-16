using System.Collections.Generic;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventMutator
    {
        IMutating MutateIncoming(IMutating mutating);
        IMutating MutateOutgoing(IMutating mutating);
    }
}
