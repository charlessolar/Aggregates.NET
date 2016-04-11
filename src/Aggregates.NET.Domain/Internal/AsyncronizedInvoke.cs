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
    internal class AsyncronizedInvoke : IBehavior<IncomingContext>
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
            var messageToHandle = context.IncomingLogicalMessage;

            var handlerType = typeof(IHandleMessagesAsync<>).MakeGenericType(messageToHandle.MessageType);
            dynamic handlers = Builder.BuildAll(handlerType);

            foreach (var handler in handlers)
                Task.Run(() => handler.Handle((dynamic)messageToHandle.Instance));

            var syncHandlerType = typeof(IHandleMessages<>).MakeGenericType(messageToHandle.MessageType);
            dynamic syncHandlers = Builder.BuildAll(syncHandlerType);

            if(syncHandlers.Any())
                foreach (var handler in syncHandlers)
                    Task.Run(() => handler.Handle((dynamic)messageToHandle.Instance));

            next();
        }
    }
}
