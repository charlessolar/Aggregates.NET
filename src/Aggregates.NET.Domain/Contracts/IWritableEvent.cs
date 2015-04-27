using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IWritableEvent
    {
        Guid EventId { get; set; }
        Object Event { get; set; }
        IEventDescriptor Descriptor { get; set; }
    }
}