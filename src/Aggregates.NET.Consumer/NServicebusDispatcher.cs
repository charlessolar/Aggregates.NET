using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates
{
    public class NServicebusDispatcher : IDispatcher
    {
        private readonly IBus _bus;

        public NServicebusDispatcher(IBus bus)
        {
            _bus = bus;
        }

        public void Dispatch(Object @event)
        {
            _bus.SendLocal(@event);
        }
    }
}