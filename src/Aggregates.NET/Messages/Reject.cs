using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface Reject : IMessage
    {
        Exception Exception { get; set; }
        String Message { get; set; }
    }
}