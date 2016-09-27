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
        void SubscribeToAll(IMessageSession bus, String endpoint);
        Boolean ProcessingLive { get; set; }
        Action<String, Exception> Dropped { get; set; }
    }
}