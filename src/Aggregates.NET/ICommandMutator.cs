using System.Collections.Generic;
using Aggregates.Contracts;
using NServiceBus;

namespace Aggregates
{
    public interface ICommandMutator
    {
        IMutating MutateIncoming(IMutating mutating);
        IMutating MutateOutgoing(IMutating mutating);
    }
}
