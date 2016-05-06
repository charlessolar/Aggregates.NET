using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Messages
{
    public interface Error : IMessage
    {
        String Message { get; set; }
        String Exception { get; set; }
    }
}
