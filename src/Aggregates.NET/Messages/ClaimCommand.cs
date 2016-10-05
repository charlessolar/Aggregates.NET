using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Messages
{
    public interface ClaimCommand : IEvent
    {
        Guid Instance { get; set; }
        String CommandType { get; set; }
        byte[] Mask { get; set; }
    }
}
