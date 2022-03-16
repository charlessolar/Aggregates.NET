using NServiceBus;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    internal class Handler : IHandleMessages<ChildNamed>
    {
        private static int _cursorLoc = 21;

        public Task Handle(ChildNamed @event, IMessageHandlerContext context)
        {
            var left = Console.CursorLeft;
            var top = Console.CursorTop;
            Console.SetCursorPosition(0, _cursorLoc);

            Console.Write($"Parent {@event.Parent} has child {@event.Name} sent: {@event.Timestamp:HH:mm:ss}");

            Console.SetCursorPosition(left, top);
            _cursorLoc++;

            return Task.CompletedTask;
        }

    }
}
