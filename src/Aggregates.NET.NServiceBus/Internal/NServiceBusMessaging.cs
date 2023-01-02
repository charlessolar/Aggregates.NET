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
        private readonly IReadOnlySettings _settings;

        public NServiceBusMessaging(MessageHandlerRegistry handlers, MessageMetadataRegistry metadata, IReadOnlySettings settings)
        {
            _handlers = handlers;
            _metadata = metadata;
            _settings = settings;
        }
        public Type[] GetHandledTypes()
        {
            return _handlers.GetMessageTypes().ToArray();
        }
        public Type[] GetMessageTypes()
        {
            // include Domain Assemblies because NSB's assembly scanning doesn't catch all types
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => !x.IsDynamic)
                .SelectMany(x => {
                    try {
                        return x.DefinedTypes.Where(IsMessageType)).ToArray();
                    } catch {}
                })
                .Distinct().ToArray();
        }
        public Type[] GetEntityTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => !x.IsDynamic)
                .SelectMany(x => {
                    try {
                        return x.DefinedTypes.Where(IsEntityType)).ToArray();
                    } catch {}
                })
                .Distinct().ToArray();
        }
        public Type[] GetStateTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(x => !x.IsDynamic)
                .SelectMany(x => {
                    try {
                        return x.DefinedTypes.Where(IsStateType)).ToArray();
                    } catch {}
                })
                .Distinct().ToArray();
        }

        public Type[] GetMessageHierarchy(Type messageType)
        {
            var metadata = _metadata.GetMessageMetadata(messageType);
            return metadata.MessageHierarchy;
        }
        private static bool IsEntityType(Type type)
        {
            if (type.IsGenericTypeDefinition)
                return false;

            return IsSubclassOfRawGeneric(typeof(Entity<,>), type);
        }
        private static bool IsStateType(Type type)
        {
            if (type.IsGenericTypeDefinition)
                return false;

            return IsSubclassOfRawGeneric(typeof(State<>), type);
        }
        private static bool IsMessageType(Type type)
        {
            if (type.IsGenericTypeDefinition)
                return false;

            return typeof(Messages.IMessage).IsAssignableFrom(type) && !typeof(IState).IsAssignableFrom(type);
        }
        // https://stackoverflow.com/a/457708/223547
        static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur)
                    return true;
                
                toCheck = toCheck.BaseType;
            }
            return false;
        }
    }
}
