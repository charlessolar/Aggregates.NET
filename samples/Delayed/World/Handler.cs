using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Attributes;
using NServiceBus;
using Shared;

namespace World
{
    [Delayed(typeof(SaidHelloALot), count: 1000)]
    class Handler : 
        IHandleMessages<SaidHello>, 
        IHandleMessages<SaidHelloALot>,
        IHandleMessages<StartedHello>,
        IHandleMessages<EndedHello>
    {
        private static int Processed = 0;
        private static DateTime? Started;

        public Task Handle(SaidHello e, IMessageHandlerContext ctx)
        {
            if (!Started.HasValue)
                Started = DateTime.UtcNow;
            Interlocked.Increment(ref Processed);

            if ((Processed%100) == 0)
            {
                var time = DateTime.UtcNow - Started.Value;
                Started = DateTime.UtcNow;
                Console.WriteLine($"-- Processing {Processed} events took {time.TotalMilliseconds} --");
            }
            
            return Task.CompletedTask;
        }
        public Task Handle(SaidHelloALot e, IMessageHandlerContext ctx)
        {
            if (!Started.HasValue)
                Started = DateTime.UtcNow;

            Interlocked.Increment(ref Processed);

            if ((Processed % 100) == 0)
            {
                var time = DateTime.UtcNow - Started.Value;
                Started = DateTime.UtcNow;
                Console.WriteLine($"-- Processing {Processed} events took {time.TotalMilliseconds} --");
            }
            return Task.CompletedTask;
        }

        public Task Handle(StartedHello e, IMessageHandlerContext ctx)
        {
            // Delayed events won't all show up before EndedHello gets here. So we'll have to time the events by counter above
            return Task.CompletedTask;
        }

        public Task Handle(EndedHello e, IMessageHandlerContext ctx)
        {
            return Task.CompletedTask;
        }
    }
}
