using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Shared
{
    public interface TradeRecorded : IEvent
    {
        string Market { get; set; }
        decimal Price { get; set; }
        decimal Amount { get; set; }
    }
}
