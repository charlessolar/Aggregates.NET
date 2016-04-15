using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IHandleContext
    {
        IEventDescriptor EventDescriptor { get; set; }
        IncomingContext Context { get; set; }
        IBus Bus { get; set; }
    }
}
