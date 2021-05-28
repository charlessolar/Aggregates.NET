using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Aggregates.Contracts;
using NServiceBus.MessageInterfaces;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    /// <summary>
    /// Small wrapper around NSB's serializer interface to direct NSB to use w/e serializer aggregates is configured with
    /// (necessary because our serializers have special things we need to do and its hard to get those things into NSB's 
    /// serializer without breaking the library apart into "Aggregates.NET.NServiceBus.JsonNet" and "Aggregates.NET.NServiceBus.Protobuf")
    /// </summary>
    [ExcludeFromCodeCoverage]
    class Serializer : NServiceBus.Serialization.IMessageSerializer
    {
        public string ContentType => _settings.MessageContentType;
        private readonly Configure _settings;
        private Lazy<IMessageSerializer> _serializer => new Lazy<IMessageSerializer>(() => _settings.Container.Resolve<IMessageSerializer>());

        public Serializer(Configure settings)
        {
            _settings = settings;
        }


        public object[] Deserialize(Stream stream, IList<Type> messageTypes = null)
        {
            return _serializer.Value.Deserialize(stream, messageTypes);
        }

        public void Serialize(object message, Stream stream)
        {
            _serializer.Value.Serialize(message, stream);
        }
    }

    [ExcludeFromCodeCoverage]
    class AggregatesSerializer : NServiceBus.Serialization.SerializationDefinition
    {
        public override Func<IMessageMapper, NServiceBus.Serialization.IMessageSerializer> Configure(ReadOnlySettings settings)
        {
            var aggSettings = settings.Get<Configure>(NSBDefaults.AggregatesSettings);

            return mapper => new Serializer(aggSettings);
        }
    }
}
