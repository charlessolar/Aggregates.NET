using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus.Pipeline;
using NServiceBus.Unicast.Messages;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class MessageIdentifier : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("MessageIdentifier");
        private MessageMetadataRegistry _registry;

        public MessageIdentifier(MessageMetadataRegistry registry)
        {
            _registry = registry;
        }

        public override Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var headers = context.MessageHeaders;
            var messageTypeKey = "NServiceBus.EnclosedMessageTypes";
            if (!headers.TryGetValue(messageTypeKey, out var messageType))
                return next();

            var mappedType = VersionRegistrar.GetNamedType(messageType);
            // no mapped type
            if (mappedType == null)
            {
                Logger.DebugEvent("UnknownMessage", "{MessageType} could not be identified", messageType);
                return next();
            }

            context.Message.Headers[messageTypeKey] = SerializeEnclosedMessageTypes(mappedType);
            return next();
        }
        string SerializeEnclosedMessageTypes(Type messageType)
        {
            var metadata = _registry.GetMessageMetadata(messageType);

            var assemblyQualifiedNames = new List<string>(metadata.MessageHierarchy.Length);
            foreach (var type in metadata.MessageHierarchy)
            {
                var typeAssemblyQualifiedName = type.AssemblyQualifiedName;
                if (assemblyQualifiedNames.Contains(typeAssemblyQualifiedName))
                {
                    continue;
                }

                assemblyQualifiedNames.Add(typeAssemblyQualifiedName);
            }

            return string.Join(";", assemblyQualifiedNames);
        }
    }
}
