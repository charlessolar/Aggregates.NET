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
            var left = Console.CursorLeft;
            var top = Console.CursorTop;
            Console.SetCursorPosition(75, 0);

            Console.Write("{0,20}", $"Hello received: {e.Message}");

            Console.SetCursorPosition(left, top - 1);


            return Task.CompletedTask;
        }
    }
}
