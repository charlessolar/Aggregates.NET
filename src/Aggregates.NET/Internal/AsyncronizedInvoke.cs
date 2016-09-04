
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
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
using Aggregates.Extensions;
using System.Threading;

namespace Aggregates.Internal
{
    class AsyncronizedInvoke : IBehavior<IncomingContext>
    {
        private static ILog Logger = LogManager.GetLogger<AsyncronizedInvoke>();
        private readonly IBus _bus;
        private readonly ReadOnlySettings _settings;
        private readonly IMessageMapper _mapper;
        private readonly Int32 _slowAlert;


        public AsyncronizedInvoke(IBus bus, ReadOnlySettings settings, IMessageMapper mapper)
        {
            _bus = bus;
            _settings = settings;
            _mapper = mapper;
            _slowAlert = _settings.Get<Int32>("SlowAlertThreshold");
            
        }

        public void Invoke(IncomingContext context, Action next)
        {
            ActiveSagaInstance saga;

            if (context.TryGet(out saga) && saga.NotFound && saga.SagaType == context.MessageHandler.Instance.GetType())
            {
                next();
                return;
            }

            Thread.Sleep(10);
            var messageHandler = context.Get<AsyncMessageHandler>();
            Task.Run((Func<Task>)(async () =>
            {
                var s = Stopwatch.StartNew();

                var handleContext = new HandleContext { Bus = _bus, Context = context, Mapper = _mapper };
                await messageHandler.Invocation(messageHandler.Handler, context.IncomingLogicalMessage.Instance, handleContext);

                if(s.ElapsedMilliseconds > _slowAlert)
                    Logger.WriteFormat(LogLevel.Warn, " - SLOW ALERT - Executing command {0} on handler {1} took {2} ms", context.IncomingLogicalMessage.MessageType.FullName, ((object)messageHandler.Handler).GetType().FullName, s.ElapsedMilliseconds);
                else
                    Logger.WriteFormat(LogLevel.Debug, "Executing command {0} on handler {1} took {2} ms", context.IncomingLogicalMessage.MessageType.FullName, ((object)messageHandler.Handler).GetType().FullName, s.ElapsedMilliseconds);
                
            })).Wait();

            next();
        }
    }
}
