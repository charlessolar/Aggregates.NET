using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.EntityRepository
{
    public class _EventStub : IWritableEvent
    {
        public IEventDescriptor Descriptor { get; set; }
        public object Event { get; set; }
        public Guid EventId { get; set; }
    }
}