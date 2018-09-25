using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class MessageDetyper : Behavior<IOutgoingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("MessageDetyper");


        public override Task Invoke(IOutgoingLogicalMessageContext context, Func<Task> next)
        {
            var messageTypeKey = "NServiceBus.EnclosedMessageTypes";
            var type = context.Message.Instance.GetType();

            //var headers = context.Headers;
            //if (!headers.TryGetValue(messageTypeKey, out var messageType))
            //    return next();

            var definition = VersionRegistrar.GetVersionedName(type);
            if (definition == null)
            {
                Logger.WarnEvent("UnknownMessage", "{MessageType} has no known definition", type.FullName);
                return next();
            }

            context.Headers[messageTypeKey] = definition;
            context.Headers[Defaults.MessageTypeHeader] = definition;

            return next();
        }
    }
}
