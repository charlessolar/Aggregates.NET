using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Settings;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    internal class NServiceBusMessaging : IMessaging
    {
        private readonly MessageHandlerRegistry _handlers;
        private readonly MessageMetadataRegistry _metadata;
        private readonly ReadOnlySettings _settings;

        public NServiceBusMessaging(MessageHandlerRegistry handlers, MessageMetadataRegistry metadata, ReadOnlySettings settings)
        {
            _handlers = handlers;
            _metadata = metadata;
            _settings = settings;
        }

        public Type[] GetMessageTypes()
        {
            // include Domain Assemblies because NSB's assembly scanning doesn't catch all types
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.DefinedTypes.Where(IsMessageType)).ToArray()
                .Concat(_settings.GetAvailableTypes().Where(IsMessageType))
                .Concat(_handlers.GetMessageTypes())
                .Distinct().ToArray();
        }
        public Type[] GetEntityTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(x => x.DefinedTypes.Where(IsEntityType)).ToArray()
                .Concat(_settings.GetAvailableTypes().Where(IsEntityType))
                .Distinct().ToArray();
        }

        public Type[] GetMessageHierarchy(Type messageType)
        {
            var metadata = _metadata.GetMessageMetadata(messageType);
            return metadata.MessageHierarchy;
        }
        private static bool IsEntityType(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
                return false;

            return type.IsSubclassOf(typeof(Entity<,>));
        }
        private static bool IsMessageType(Type type)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition)
                return false;

            return typeof(Messages.IMessage).IsAssignableFrom(type);
        }
    }
}
