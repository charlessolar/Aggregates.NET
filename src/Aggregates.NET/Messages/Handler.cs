using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Messages
{
    internal class Handler :
        IHandleMessages<Accept>,
        IHandleMessages<Reject>
    {
        public void Handle(Accept e) { }
        public void Handle(Reject e) { }
    }
}
