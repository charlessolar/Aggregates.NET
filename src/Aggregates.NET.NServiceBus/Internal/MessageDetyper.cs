using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class MessageDetyper : Behavior<IOutgoingPhysicalMessageContext>
    {
        private readonly ILogger Logger;
        private readonly Contracts.IVersionRegistrar _registrar;
        private readonly Contracts.IEventMapper _mapper;

        public MessageDetyper(ILoggerFactory logFactory, Contracts.IVersionRegistrar registrar, IEventMapper mapper)
        {
            Logger = logFactory.CreateLogger("MessageDetyper");
            _registrar = registrar;
            _mapper = mapper;
        }

        public override Task Invoke(IOutgoingPhysicalMessageContext context, Func<Task> next)
        {
            var messageTypeKey = global::NServiceBus.Headers.EnclosedMessageTypes;
            //var headers = context.Headers;
            if (!context.Headers.TryGetValue(messageTypeKey, out var messageType))
                return next();

            if(messageType.IndexOf(';') != -1)
                messageType = messageType.Substring(0, messageType.IndexOf(';'));
            
            // Don't use context.Message.Instance because it will be IEvent_impl
            var type = Type.GetType(messageType, false);
            if(type == null)
            {
                Logger.WarnEvent("UnknownType", "{MessageType} sent - but could not load type?", messageType);
                return next();
            }
            if (!type.IsInterface)
                type = _mapper.GetMappedTypeFor(type) ?? type;

            var definition = _registrar.GetVersionedName(type);
            if (definition == null)
            {
                Logger.WarnEvent("UnknownMessage", "{MessageType} has no known definition", type.FullName);
                return next();
            }

            context.Headers[messageTypeKey] = definition;

            return next();
        }
    }
    [ExcludeFromCodeCoverage]
    internal class MessageDetyperRegistration : RegisterStep
    {
        public MessageDetyperRegistration() : base(
            stepId: "MessageDetyper",
            behavior: typeof(MessageDetyper),
            description: "detypes outgoing messages to Versioned commands/events",
            factoryMethod: (b) => new MessageDetyper(b.GetService<ILoggerFactory>(), b.GetService<Contracts.IVersionRegistrar>(), b.GetService<Contracts.IEventMapper>()))
        {
        }
    }
}
