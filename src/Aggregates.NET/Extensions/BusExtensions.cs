using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class BusExtensions
    {
        public static void ReplyAsync(this IBus bus, object message)
        {
            var incoming = MessageRegistry.Current;
            bus.Send(incoming.ReplyToAddress, incoming.CorrelationId, message);
        }
        public static void ReplyAsync<T>(this IBus bus, Action<T> message)
        {
            var incoming = MessageRegistry.Current;
            bus.Send(incoming.ReplyToAddress, incoming.CorrelationId, message);
        }
    }
}
