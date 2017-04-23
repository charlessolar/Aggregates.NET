using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class BlockOobEvents : Behavior<IIncomingLogicalMessageContext>
    {
        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            // If receiving an oob stream, wait for PauseOob which is a CompletedTask UNLESS
            // we are currently processing domain events
            // (oob events are second class citizens)
            if (context.Headers.ContainsKey("StreamType") && context.Headers["StreamType"] == StreamTypes.OOB)
                await Bus.PauseOob().ConfigureAwait(false);
            await next().ConfigureAwait(false);
        }
    }
    internal class BlockOobEventsRegistration : RegisterStep
    {
        public BlockOobEventsRegistration() : base(
            stepId: "BlockOobEvents",
            behavior: typeof(BlockOobEvents),
            description: "Pauses an oob event's execution if currently processing domain events"
        )
        {
            InsertBeforeIfExists("ApplicationUnitOfWork");
        }
    }
}
