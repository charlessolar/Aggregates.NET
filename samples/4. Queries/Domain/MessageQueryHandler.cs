using Aggregates;
using NServiceBus;
using Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    // This class responds to messages representing queries
    // a remote service can send us `MessageTotal` request and get a reply
    // via NSB callbacks.
    // Also any other message handler can do `ctx.Service<MessageQuery, int>` to
    // get the query result.
    //
    // This is a simple example using a static variable
    // but in real practice you could use an external service like
    // mongo, sql, w/e to get the query result.
    public class MessageQueryHandler :
        IHandleMessages<Echo>,
        IHandleMessages<MessageTotal>,
        IProvideService<MessageQuery, int>
    {

        public static int TotalEchos = 0;

        // Keep a count of echos that have been seen
        public Task Handle(Echo message, IMessageHandlerContext context)
        {
            TotalEchos++;
            return Task.CompletedTask;
        }

        // remote query handling
        public async Task Handle(MessageTotal request, IMessageHandlerContext ctx)
        {
            // This method searches for IProvideService<MessageQuery, int> to get the result
            var messages = await ctx.Service<MessageQuery, int>(new MessageQuery()).ConfigureAwait(false);

            // Using NSB callbacks
            await ctx.Reply<MessageTotalResponse>(x =>
            {
                x.Total = messages;
            }).ConfigureAwait(false);
        }

        // in process query handling
        public Task<int> Handle(MessageQuery query, IServiceContext context)
        {
            return Task.FromResult(TotalEchos);
        }

    }
}
