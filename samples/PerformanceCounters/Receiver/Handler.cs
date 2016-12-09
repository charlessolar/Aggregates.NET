using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;
using Shared;

namespace World
{
    class Handler : IHandleMessages<Event>
    {
        public Task Handle(Event e, IMessageHandlerContext ctx)
        {
            Console.WriteLine($"User {e.User} says {e.Message}");
            return Task.CompletedTask;
        }
    }
}
