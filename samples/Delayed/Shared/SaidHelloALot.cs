using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public interface SaidHelloALot : IEvent
    {
        String User { get; set; }
        String Message { get; set; }
    }
}
