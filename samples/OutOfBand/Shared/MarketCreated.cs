using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public interface MarketCreated : IEvent
    {
        string Market { get; set; }
        decimal Bid { get; set; }
        decimal Ask { get; set; }
    }
}
