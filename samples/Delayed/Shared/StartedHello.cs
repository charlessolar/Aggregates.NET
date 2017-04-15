using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public interface StartedHello : IEvent
    {
        string User { get; set; }
        DateTime Timestamp { get; set; }
    }
}
