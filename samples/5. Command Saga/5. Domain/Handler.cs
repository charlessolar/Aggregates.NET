using Aggregates;
using Language;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class Handler :
        IHandleMessages<SayHello>,
        IHandleMessages<Echo>,
        IHandleMessages<SaidHello>
    {
        public async Task Handle(SayHello command, IMessageHandlerContext ctx)
        {
            var world = await ctx.For<World>().TryGet("World");
            if (world == null)
                world = await ctx.For<World>().New("World");

            world.SayHello(command.Message);

        }
        public Task Handle(Echo command, IMessageHandlerContext ctx)
        {
            Console.WriteLine($"Received echo: {command.Message}");
            return Task.CompletedTask;
        }
        public async Task Handle(SaidHello e, IMessageHandlerContext ctx)
        {

            var saga = ctx.Saga(e.Message)
                .Command(new Echo
                {
                    Message = "One"
                })
                .Command(new Echo
                {
                    Message = "Two"
                });

            await saga.Start().ConfigureAwait(false);
        }
    }
}
