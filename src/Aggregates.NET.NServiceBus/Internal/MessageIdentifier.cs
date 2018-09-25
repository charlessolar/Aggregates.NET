using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class MessageIdentifier : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("MessageIdentifier");


        public override Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var headers = context.MessageHeaders;
            var messageTypeKey = "NServiceBus.EnclosedMessageTypes";
            if (!headers.TryGetValue(messageTypeKey, out var messageType))
                return next();
            
            if (!headers.TryGetValue(Defaults.MessageVersionHeader, out var messageVersionStr) || !int.TryParse(messageVersionStr, out var messageVersion))
                messageVersion = 1;

            var mappedType = VersionRegistrar.GetNamedType(messageType, messageVersion);
            // no mapped type
            if (mappedType == null)
            {
                Logger.DebugEvent("UnknownMessage", "{MessageType} could not be identified", messageType);
                return next();
            }

            context.Message.Headers[messageTypeKey] = mappedType.AssemblyQualifiedName;
            return next();
        }
    }
}
