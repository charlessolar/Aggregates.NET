using System.Collections.Generic;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface ICommandMutator
    {
        ICommand MutateIncoming(ICommand command, IReadOnlyDictionary<string, string> headers);
        ICommand MutateOutgoing(ICommand command);
    }
}
