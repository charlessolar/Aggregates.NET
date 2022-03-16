using Aggregates;
using Aggregates.Domain;
using NServiceBus;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    internal class Handler :
        IHandleMessages<Send>,
        IHandleMessages<SagaSend>,
        IHandleMessages<Echo>
    {
        public async Task Handle(Send command, IMessageHandlerContext ctx)
        {
            var entity = await ctx.For<EchoEntity>().TryGet("default");
            if (entity == null)
                entity = await ctx.For<EchoEntity>().New("default");

            entity.Echo(command.Message);
        }
        public async Task Handle(SagaSend command, IMessageHandlerContext ctx)
        {
            var entity = await ctx.For<EchoEntity>().Get("default");

            entity.SagaEcho(command.Message);
        }
        public async Task Handle(Echo e, IMessageHandlerContext ctx)
        {
            Console.WriteLine($"Received echo: {e.Message}");

            // starts a saga which will execute the commands in series
            var saga = ctx.Saga(e.Message)
                .Command(new SagaSend
                {
                    Message = $"{e.Message} One"
                })
                .Command(new SagaSend
                {
                    Message = $"{e.Message} Two"
                });

            await saga.Start().ConfigureAwait(false);
        }
    }
}
