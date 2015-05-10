using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates
{
    public interface IEventSubscriber
    {
        void SubscribeToAll(String endpoint, IDispatcher dispatcher);
    }
}