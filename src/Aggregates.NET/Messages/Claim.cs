using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Messages
{
    public interface Claim : IEvent
    {
        String Endpoint { get; set; }
        Guid Instance { get; set; }
        String Queue { get; set; }
        String CommandType { get; set; }
    }
}
