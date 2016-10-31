using System;
using Newtonsoft.Json.Serialization;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;

namespace Aggregates.Internal
{
    public class EventContractResolver : DefaultContractResolver
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventContractResolver));

        private readonly IMessageMapper _mapper;

        public EventContractResolver(IMessageMapper mapper) : base(true)
        {
            _mapper = mapper;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var mappedTypeFor = objectType;
            if (!mappedTypeFor.IsInterface)
                mappedTypeFor = _mapper.GetMappedTypeFor(objectType);
            
            if (mappedTypeFor == null)
                return base.CreateObjectContract(objectType);

            var objectContract = base.CreateObjectContract(mappedTypeFor);

            objectContract.DefaultCreator = () => _mapper.CreateInstance(mappedTypeFor);

            return objectContract;
        }
    }

    public class EventSerializationBinder : DefaultSerializationBinder
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventSerializationBinder));

        private readonly IMessageMapper _mapper;

        public EventSerializationBinder(IMessageMapper mapper)
        {
            _mapper = mapper;
        }

        public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            var mappedType = _mapper.GetMappedTypeFor(serializedType) ?? serializedType;

            assemblyName = null;
            typeName = mappedType.AssemblyQualifiedName;
        }
    }
}