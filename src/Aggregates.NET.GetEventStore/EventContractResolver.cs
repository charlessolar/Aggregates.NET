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
        private readonly IMessageCreator _creator;

        public EventContractResolver(IMessageMapper mapper, IMessageCreator creator)
        {
            _mapper = mapper;
            _creator = creator;
        }

        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var mappedTypeFor = _mapper.GetMappedTypeFor(objectType);

            if (mappedTypeFor == null)
                return base.CreateObjectContract(objectType);

            var objectContract = base.CreateObjectContract(mappedTypeFor);

            objectContract.DefaultCreator = () => _creator.CreateInstance(mappedTypeFor);

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
            var mappedType = _mapper.GetMappedTypeFor(serializedType);

            assemblyName = mappedType.Assembly.FullName;
            typeName = mappedType.FullName;
        }
    }
}