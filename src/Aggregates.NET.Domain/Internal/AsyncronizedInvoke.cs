using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Sagas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    internal class AsyncronizedInvoke : IBehavior<IncomingContext>
    {
        public void Invoke(IncomingContext context, Action next)
        {
            ActiveSagaInstance saga;

            if (context.TryGet(out saga) && saga.NotFound && saga.SagaType == context.MessageHandler.Instance.GetType())
            {
                next();
                return;
            }

            var messageHandler = context.MessageHandler;

            Task.Run(() => messageHandler.Invocation(messageHandler.Instance, context.IncomingLogicalMessage.Instance));
            next();
        }
    }
}
