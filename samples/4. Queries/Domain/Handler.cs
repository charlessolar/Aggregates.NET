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
        IHandleMessages<Send>
    {
        public async Task Handle(Send command, IMessageHandlerContext ctx)
        {
            var entity = await ctx.For<EchoEntity>().TryGet("default");
            if (entity == null)
                entity = await ctx.For<EchoEntity>().New("default");

            entity.Echo(command.Message);
        }
    }
}
