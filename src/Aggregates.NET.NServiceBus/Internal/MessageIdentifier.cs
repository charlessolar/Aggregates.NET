using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus.Pipeline;
using NServiceBus.Unicast.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class MessageIdentifier : Behavior<IIncomingPhysicalMessageContext>
    {
        private readonly ILogger Logger;
        private readonly MessageMetadataRegistry _registry;
        private readonly Contracts.IVersionRegistrar _registrar;

        public MessageIdentifier(ILoggerFactory logFactory, MessageMetadataRegistry registry, Contracts.IVersionRegistrar registrar)
        {
            Logger = logFactory.CreateLogger("MessageIdentifier");
            _registry = registry;
            _registrar = registrar;
        }

        public override Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var headers = context.MessageHeaders;
            var messageTypeKey = "NServiceBus.EnclosedMessageTypes";
            if (!headers.TryGetValue(messageTypeKey, out var messageType))
                return next();

            // if enclosed type is a full type
            if (messageType.IndexOf(';') != -1)
                return next();

            var mappedType = _registrar.GetNamedType(messageType);
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
    [ExcludeFromCodeCoverage]
    internal class MessageIdentifierRegistration : RegisterStep
    {
        public MessageIdentifierRegistration() : base(
            stepId: "MessageIdentifier",
            behavior: typeof(MessageIdentifier),
            description: "identifies incoming messages as Versioned commands/events",
            factoryMethod: (b) => new MessageIdentifier(b.Build<ILoggerFactory>(), b.Build<MessageMetadataRegistry>(), b.Build<Contracts.IVersionRegistrar>()))
        {
        }
    }
}
