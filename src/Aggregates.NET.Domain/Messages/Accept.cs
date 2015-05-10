using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates.Messages
{
    public interface Accept : IMessage
    {
    }
}