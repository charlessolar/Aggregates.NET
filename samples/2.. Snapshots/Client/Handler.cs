using NServiceBus;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    internal class Handler : IHandleMessages<Echo>
    {
        public Task Handle(Echo @event, IMessageHandlerContext context)
        {
            var left = Console.CursorLeft;
            var top = Console.CursorTop;
            Console.SetCursorPosition(0, 17);

            Console.Write($"Received: {@event.Message} sent: {@event.Timestamp:HH:mm:ss}");

            Console.SetCursorPosition(left, top - 1);


            return Task.CompletedTask;
        }

    }
}
