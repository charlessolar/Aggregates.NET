using System.Collections.Generic;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface ICommandMutator
    {
        IMutating MutateIncoming(IMutating mutating);
        IMutating MutateOutgoing(IMutating mutating);
    }
}
