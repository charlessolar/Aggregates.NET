using NServiceBus;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface ICommandMutator
    {
        ICommand MutateIncoming(ICommand command);
        ICommand MutateOutgoing(ICommand command);
    }
}
