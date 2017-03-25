using System;
using Newtonsoft.Json.Serialization;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;

namespace Aggregates.Internal
{
    class EventContractResolver : DefaultContractResolver
    {
        private readonly IMessageMapper _mapper;

        public EventContractResolver(IMessageMapper mapper)
        {
            _mapper = mapper;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var mappedTypeFor = objectType;
            if (mappedTypeFor.IsInterface)
                mappedTypeFor = _mapper.GetMappedTypeFor(objectType);
            
            if (mappedTypeFor == null)
                return base.CreateObjectContract(objectType);

            var objectContract = base.CreateObjectContract(mappedTypeFor);

            objectContract.DefaultCreator = () => _mapper.CreateInstance(mappedTypeFor);

            return objectContract;
        }
    }

    class EventSerializationBinder : DefaultSerializationBinder
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventSerializationBinder));

        private readonly IMessageMapper _mapper;

        public EventSerializationBinder(IMessageMapper mapper)
        {
            _mapper = mapper;
        }

        public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            var mappedType = serializedType;
            if(!serializedType.IsInterface)
                mappedType = _mapper.GetMappedTypeFor(serializedType) ?? serializedType;

            assemblyName = null;
            typeName = mappedType.AssemblyQualifiedName;
        }
    }
}