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
    [Delayed(typeof(SaidHelloALot), Count: 1000)]
    class Handler : 
        IHandleMessages<SaidHello>, 
        IHandleMessages<SaidHelloALot>,
        IHandleMessages<Time>
    {
        private static int Processed = 0;
        private static DateTime? Started;

        public Task Handle(SaidHello e, IMessageHandlerContext ctx)
        {
            Processed++;
            return Task.CompletedTask;
        }
        public Task Handle(SaidHelloALot e, IMessageHandlerContext ctx)
        {
            Processed++;
            return Task.CompletedTask;
        }

        public Task Handle(Time e, IMessageHandlerContext ctx)
        {
            if (e.Start.HasValue)
                Started = e.Start;
            else
            {
                var time = DateTime.UtcNow - Started.Value;

                Console.WriteLine($"-- Processing {Processed} events took {time.TotalMilliseconds} --");
                Processed = 0;
            }
            return Task.CompletedTask;
        }
    }
}
