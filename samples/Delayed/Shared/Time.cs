using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public class Time : IEvent
    {
        public DateTime? Start { get; set; }
    }
}
