using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class FixSendIntent : IBehavior<OutgoingContext>
    {
        public void Invoke(OutgoingContext context, Action next)
        {
            if (context.OutgoingLogicalMessage.Headers.ContainsKey("$.Aggregates.Replying"))
            {
                context.OutgoingMessage.MessageIntent = MessageIntentEnum.Reply;
                context.OutgoingMessage.Headers.Remove("$.Aggregates.Replying");
                context.OutgoingLogicalMessage.Headers.Remove("$.Aggregates.Replying");
            }

            next();
        }
    }
    internal class FixSendIntentRegistration : RegisterStep
    {
        public FixSendIntentRegistration()
            : base("AggregatesFixSendIntent", typeof(FixSendIntent), "Fixes outgoing message's intent when using ReplyAsync")
        {
            InsertAfter(WellKnownStep.CreatePhysicalMessage);
            InsertBefore(WellKnownStep.SerializeMessage);

        }
    }
}
