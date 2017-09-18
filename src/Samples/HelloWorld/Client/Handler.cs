using Language;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public class Handler :
        IHandleMessages<SaidHello>
    {
        public Task Handle(SaidHello e, IMessageHandlerContext ctx)
        {
            Console.WriteLine($"Hello received: {e.Message}");
            return Task.CompletedTask;
        }
    }
}
