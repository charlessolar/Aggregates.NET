using NServiceBus;
using NServiceBus.ObjectBuilder;
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
    public class AsyncronizedInvoke : IBehavior<IncomingContext>
    {
        public IBuilder Builder { get; set; }
        public void Invoke(IncomingContext context, Action next)
        {

            ActiveSagaInstance saga;

            if (context.TryGet(out saga) && saga.NotFound && saga.SagaType == context.MessageHandler.Instance.GetType())
            {
                next();
                return;
            }
            
            var messageHandler = context.Get<AsyncMessageHandler>();
            Task.Run((Func<Task>)(async () =>
            {
                var message = context.PhysicalMessage;
                await messageHandler.Invocation(messageHandler.Handler, context.IncomingLogicalMessage.Instance);
            })).Wait();
            
            next();
        }
    }
}
