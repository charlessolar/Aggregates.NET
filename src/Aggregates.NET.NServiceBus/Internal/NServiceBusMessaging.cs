using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    internal class NServiceBusMessaging : IMessaging
    {
        private readonly MessageHandlerRegistry _handlers;
        private readonly MessageMetadataRegistry _metadata;

        public NServiceBusMessaging(MessageHandlerRegistry handlers, MessageMetadataRegistry metadata)
        {
            _handlers = handlers;
            _metadata = metadata;
        }

        public IEnumerable<Type> GetMessageTypes()
        {
            return _handlers.GetMessageTypes();
        }

        public IEnumerable<Type> GetMessageHierarchy(Type messageType)
        {
            var metadata = _metadata.GetMessageMetadata(messageType);
            return metadata.MessageHierarchy;
        }
    }
}
