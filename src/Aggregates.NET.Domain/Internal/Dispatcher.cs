using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class Dispatcher : IDispatcher
    {
        protected readonly IBus _bus;

        public Dispatcher(IBus bus)
        {
            _bus = bus;
        }

        public void Dispatch(IWritableEvent @event)
        {
            _bus.OutgoingHeaders.Merge(@event.Descriptor.ToDictionary());
            _bus.SendLocal(@event.Event);
        }
    }
}
