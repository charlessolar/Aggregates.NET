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
            //var headers = context.Headers;
            if (!context.Headers.TryGetValue(messageTypeKey, out var messageType))
                return next();

            // Don't use context.Message.Instance because it will be IEvent_impl
            var type = Type.GetType(messageType, false);
            if(type == null)
            {
                Logger.WarnEvent("UnknownType", "{MessageType} sent - but could not load type?", messageType);
                return next();
            }

            var definition = VersionRegistrar.GetVersionedName(type);
            if (definition == null)
            {
                Logger.WarnEvent("UnknownMessage", "{MessageType} has no known definition", type.FullName);
                return next();
            }

            context.Headers[messageTypeKey] = definition;

            return next();
        }
    }
}
