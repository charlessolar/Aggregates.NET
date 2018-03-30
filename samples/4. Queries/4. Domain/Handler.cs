using Aggregates;
using Aggregates.Contracts;
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
        IHandleMessages<SayHello>,
        IHandleMessages<SaidHello>,
        IHandleMessages<RequestMessages>,
        IHandleQueries<PreviousMessages, MessageState[]>
    {
        // Typically from some external storage
        private readonly static List<Guid> MessageIds = new List<Guid>();

        public async Task Handle(SayHello command, IMessageHandlerContext ctx)
        {
            var world = await ctx.For<World>().TryGet("World");
            if (world == null)
            {
                world = await ctx.For<World>().New("World");
                world.Create();
            }

            var message = await world.For<Message>().New(Guid.NewGuid());
            message.SayHello(command.Message);

        }
        // Listening to events allowed - but you can't modify entities
        // typically used to update domain storage
        public Task Handle(SaidHello e, IMessageHandlerContext ctx)
        {
            MessageIds.Add(e.MessageId);
            return Task.CompletedTask;
        }

        // sent from clients via bus who want a list of previous messages
        public async Task Handle(RequestMessages request, IMessageHandlerContext ctx)
        {
            var messages = await ctx.Query<PreviousMessages, MessageState[]>(new PreviousMessages()).ConfigureAwait(false);

            // Using NSB callbacks
            await ctx.Reply(new MessagesResponse { Messages = messages.Select(x => x.Message).ToArray() }).ConfigureAwait(false);
        }

        public async Task<MessageState[]> Handle(PreviousMessages query, IHandleContext context)
        {
            var uow = context.UoW;

            // Can use uow to get domain entities
            var world = await uow.For<World>().TryGet("World");
            if (world == null)
                return new MessageState[] { };

            var states = new List<MessageState>();
            foreach(var id in MessageIds)
            {
                var message = await world.For<Message>().Get(id).ConfigureAwait(false);
                states.Add(message.State);
            }
            return states.ToArray();
        }

    }
}
