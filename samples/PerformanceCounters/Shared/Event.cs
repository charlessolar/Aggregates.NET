using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public interface Event : IEvent
    {
        Guid Guid { get; set; }
        string User { get; set; }
        string Message { get; set; }
    }
}
