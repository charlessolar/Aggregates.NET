using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            if (!(@event is IMessage)) return;
            _bus.SendLocal(@event);
        }
    }
}