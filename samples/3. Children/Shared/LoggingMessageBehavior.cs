using Microsoft.Extensions.Logging;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public class IncomingLoggingMessageBehavior : Behavior<IIncomingLogicalMessageContext>
    {
        private readonly ILogger<IncomingLoggingMessageBehavior> _logger;
        public IncomingLoggingMessageBehavior(ILogger<IncomingLoggingMessageBehavior> logger)
        {
            _logger = logger;
        }

        public override Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {

            
            _logger.LogDebug("<{EventId:l}> Received message '{MessageType}'.\n" +
                            "ToString() of the message yields: {MessageBody}\n" +
                            "Message headers:\n{MessageHeaders}", "Incoming",
                            context.Message.MessageType != null ? context.Message.MessageType.AssemblyQualifiedName : "unknown",
                context.Message.Instance,
                string.Join(", ", context.MessageHeaders.Select(h => h.Key + ":" + h.Value).ToArray()));

            return next();
        }

    }
    public class OutgoingLoggingMessageBehavior : Behavior<IOutgoingLogicalMessageContext>
    {
        private readonly ILogger<OutgoingLoggingMessageBehavior> _logger;
        public OutgoingLoggingMessageBehavior(ILogger<OutgoingLoggingMessageBehavior> logger)
        {
            _logger = logger;
        }

        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {

            _logger.LogDebug("<{EventId:l}> Sending message '{MessageType}'.\n" +
                            "ToString() of the message yields: {MessageBody}\n" +
                            "Message headers:\n{MessageHeaders}", "Outgoing",
                            context.Message.MessageType != null ? context.Message.MessageType.AssemblyQualifiedName : "unknown",
                context.Message.Instance,
                string.Join(", ", context.Headers.Select(h => h.Key + ":" + h.Value).ToArray()));

            return next();
        }

    }
}
