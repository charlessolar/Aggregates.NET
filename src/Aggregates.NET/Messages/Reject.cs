using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;
using Aggregates.Exceptions;

namespace Aggregates.Messages
{
    public interface Reject : IMessage
    {
        BusinessException Exception { get; set; }
        String Message { get; set; }
    }
}