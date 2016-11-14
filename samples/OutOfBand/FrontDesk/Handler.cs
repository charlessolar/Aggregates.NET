using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Attributes;
using NServiceBus;
using Shared;

namespace World
{
    class Handler : 
        IHandleMessages<MarketCreated>, 
        IHandleMessages<TradeRecorded>
    {
        public Task Handle(MarketCreated e, IMessageHandlerContext ctx)
        {
            Console.WriteLine($"-- New market: {e.Market} --");
            return Task.CompletedTask;
        }
        public Task Handle(TradeRecorded e, IMessageHandlerContext ctx)
        {
            Console.WriteLine($"-- Trade: {e.Market} {e.Price} x {e.Amount}");
            return Task.CompletedTask;
        }
        
    }
}
