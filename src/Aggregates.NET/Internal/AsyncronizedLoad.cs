using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Unicast.Behaviors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class AsyncronizedLoad : IBehavior<IncomingContext>
    {
        private readonly IInvokeObjects _invoker;

        private static MethodInfo _snapshotRegion = typeof(IncomingContext).GetMethod("CreateSnapshotRegion", BindingFlags.NonPublic | BindingFlags.Instance);
        private static Func<IncomingContext, IDisposable> CreateSnapshotRegion = (context) => (IDisposable)_snapshotRegion.Invoke(context, null);

        public AsyncronizedLoad(IInvokeObjects invoker)
        {
            _invoker = invoker;
        }

        public void Invoke(IncomingContext context, Action next)
        {
            Invoke(new IncomingContextWrapper(context), next);
        }

        public void Invoke(IIncomingContextAccessor context, Action next)
        {
            var callbackInvoked = context.Get<bool>("NServiceBus.CallbackInvocationBehavior.CallbackWasInvoked");

            var handlerGenericType = typeof(IHandleMessagesAsync<>).MakeGenericType(context.IncomingLogicalMessageMessageType);
            List<dynamic> handlers = context.Builder.BuildAll(handlerGenericType).ToList();



            if (!callbackInvoked && !handlers.Any())
            {
                var error = string.Format("No handlers could be found for message type: {0}", context.IncomingLogicalMessageMessageType);
                throw new InvalidOperationException(error);
            }

            foreach (var handler in handlers)
            {
                IDisposable snapshotRegion = null;
                if (context is IncomingContext)
                    snapshotRegion = CreateSnapshotRegion(context as IncomingContext);

                var lambda = _invoker.Invoker(handler, context.IncomingLogicalMessageMessageType);

                var loadedHandler = new AsyncMessageHandler
                {
                    Handler = handler,
                    Invocation = lambda
                };

                context.Set(loadedHandler);

                next();

                if (context.HandlerInvocationAborted)
                {
                    //if the chain was aborted skip the other handlers
                    break;
                }

                if (context is IncomingContext)
                    snapshotRegion.Dispose();
            }


        }
    }

    public class AsyncMessageHandler
    {
        public dynamic Handler { get; set; }
        public Func<dynamic, object, IHandleContext, Task> Invocation { get; set; }
    }
}
