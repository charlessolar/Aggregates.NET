using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventMutator
    {
        Object MutateIncoming(Object Event, IEventDescriptor Descriptor, long? Position);
        IWritableEvent MutateOutgoing(IWritableEvent Event);
    }
}
