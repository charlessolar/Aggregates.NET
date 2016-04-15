
using NServiceBus;
using NServiceBus.Logging;
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
        private static ILog Logger = LogManager.GetLogger<AsyncronizedInvoke>();
        public IBuilder Builder { get; set; }
        public IBus Bus { get; set; }
        public void Invoke(IncomingContext context, Action next)
        {
            ActiveSagaInstance saga;

            if (context.TryGet(out saga) && saga.NotFound && saga.SagaType == context.MessageHandler.Instance.GetType())
            {
                next();
                return;
            }

            var messageHandler = context.Get<AsyncMessageHandler>();
            //messageHandler.Invocation(messageHandler.Handler, context.IncomingLogicalMessage.Instance).Wait();
            Task.Run((Func<Task>)(async () =>
            {
                var handleContext = new HandleContext { Bus = Bus, Context = context };
                await messageHandler.Invocation(messageHandler.Handler, context.IncomingLogicalMessage.Instance, handleContext);
                
            })).Wait();

            next();
        }
    }
}
