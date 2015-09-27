using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.Unicast;
using NServiceBus.Settings;
using NServiceBus.Logging;

namespace Aggregates
{
    public class NServiceBusDispatcher : IDispatcher
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(NServiceBusDispatcher));
        private readonly IBus _bus;
        private readonly IMessageHandlerRegistry _handlerRegistry;
        private readonly IBuilder _builder;

        public NServiceBusDispatcher(IBus bus, IBuilder builder)
        {
            _bus = bus;
            _builder = builder;
            _handlerRegistry = builder.Build<IMessageHandlerRegistry>();
        }

        public void Dispatch(Object @event)
        {
            // We can't publish unstructured POCOs
            if (@event is JObject) return;

            // Use NSB internal handler registry to directly call Handle(@event)
            // This will prevent the event from being queued on MSMQ
            var handlersToInvoke = _handlerRegistry.GetHandlerTypes(@event.GetType()).ToList();

            foreach (var handlerType in handlersToInvoke)
            {
                var start = DateTime.UtcNow;
                var handler = _builder.Build(handlerType);
                _handlerRegistry.InvokeHandle(handler, @event);
                var duration = (DateTime.UtcNow - start).TotalMilliseconds;
                Logger.DebugFormat("Dispatching event {0} to handler {1} took {2} milliseconds", @event.GetType(), handlerType, duration);
            }
                //_bus.Publish(@event);
        }
    }
}