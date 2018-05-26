using Aggregates;
using Language;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Handler :
        IHandleMessages<SayHello>
    {
        public async Task Handle(SayHello command, IMessageHandlerContext ctx)
        {
            var world = await ctx.For<World>().TryGet("World");
            if (world == null)
            {
                world = await ctx.For<World>().New("World");
                world.Create();
            }

            var message = await world.For<Message>().New(command.MessageId);
            message.SayHello(command.Message);

        }

    }
}
