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
        public static void ReplyAsync(this IHandleContext context, object message)
        {
            var incoming = context.Context.PhysicalMessage;
            context.Bus.SetMessageHeader(message, "$.Aggregates.Replying", "1");
            var replyTo = incoming.ReplyToAddress.ToString();
            // Special case if using RabbitMq - replies need to be sent to the CallbackQueue NOT the primary queue
            if (context.Context.PhysicalMessage.Headers.ContainsKey("NServiceBus.RabbitMQ.CallbackQueue"))
                replyTo = context.Context.PhysicalMessage.Headers["NServiceBus.RabbitMQ.CallbackQueue"];
            

            context.Bus.Send(Address.Parse(replyTo), String.IsNullOrEmpty(incoming.CorrelationId) ? incoming.Id : incoming.CorrelationId, message);
        }
        public static void ReplyAsync<T>(this IHandleContext context, Action<T> message)
        {
            context.ReplyAsync(context.Mapper.CreateInstance(message));
        }
        public static void PublishAsync<T>(this IHandleContext context, Action<T> message)
        {
            context.Bus.Publish<T>(message);
        }
        public static void PublishAsync(this IHandleContext context, object message)
        {
            context.Bus.Publish(message);
        }
        public static void SendAsync<T>(this IHandleContext context, Action<T> message)
        {
            context.Bus.Send(message);
        }
        public static void SendAsync(this IHandleContext context, object message)
        {
            context.Bus.Send(message);
        }
        public static void SendAsync<T>(this IHandleContext context, String destination, Action<T> message)
        {
            context.Bus.Send(destination, message);
        }
        public static void SendAsync(this IHandleContext context, String destination, object message)
        {
            context.Bus.Send(destination, message);
        }
    }
}
