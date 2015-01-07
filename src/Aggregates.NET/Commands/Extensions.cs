using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Commands
{
    public static class Extensions
    {
        public static void Accept(this IBus bus)
        {
            bus.Reply<Accept>(e => { });
        }
        public static void Reject(this IBus bus, String Message)
        {
            bus.Reply<Reject>(e => { e.Message = Message; });
        }
    }
}
