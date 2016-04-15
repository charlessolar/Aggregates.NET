using NServiceBus;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class HandleContext : IHandleContext
    {
        public IncomingContext Context { get; set; }
        public IBus Bus { get; set; }
    }
}
