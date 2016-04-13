using Aggregates.Contracts;
using Newtonsoft.Json.Serialization;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
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
            var mappedTypeFor = _mapper.GetMappedTypeFor(objectType);
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