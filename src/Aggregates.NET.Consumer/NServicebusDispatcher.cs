using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NServiceBus;

namespace Aggregates
{
    public class NServiceBusDispatcher : IDispatcher
    {
        private readonly IBus _bus;

        public NServiceBusDispatcher(IBus bus)
        {
            _bus = bus;
        }

        public void Dispatch(Object @event)
        {
            // We can't publish unstructured POCOs
            if (@event is JObject) return;
            _bus.Publish(@event);
        }
    }
}