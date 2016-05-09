
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Sagas;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class AsyncronizedInvoke : IBehavior<IncomingContext>
    {
        private static ILog Logger = LogManager.GetLogger<AsyncronizedInvoke>();
        public IBus Bus { get; set; }
        public ReadOnlySettings Settings { get; set; }
        public void Invoke(IncomingContext context, Action next)
        {
            var _slowAlert = Settings.Get<Int32>("SlowAlertThreshold");
            ActiveSagaInstance saga;

            if (context.TryGet(out saga) && saga.NotFound && saga.SagaType == context.MessageHandler.Instance.GetType())
            {
                next();
                return;
            }

            var messageHandler = context.Get<AsyncMessageHandler>();
            //var handleContext = new HandleContext { Bus = Bus, Context = context };
            //messageHandler.Invocation(messageHandler.Handler, context.IncomingLogicalMessage.Instance, handleContext).Wait();
            Task.Run((Func<Task>)(async () =>
            {
                var s = Stopwatch.StartNew();

                var handleContext = new HandleContext { Bus = Bus, Context = context };
                await messageHandler.Invocation(messageHandler.Handler, context.IncomingLogicalMessage.Instance, handleContext);

                if (Logger.IsDebugEnabled)
                {
                    Logger.DebugFormat("Executing message {0} on handler {1} took {2} ms", context.IncomingLogicalMessage.MessageType.FullName, messageHandler.Handler.GetType().FullName, s.ElapsedMilliseconds);
                }
                if(s.ElapsedMilliseconds > _slowAlert)
                {
                    Logger.DebugFormat(" - SLOW ALERT - Executing message {0} on handler {1} took {2} ms", context.IncomingLogicalMessage.MessageType.FullName, messageHandler.Handler.GetType().FullName, s.ElapsedMilliseconds);
                }
            })).Wait();

            next();
        }
    }
}
