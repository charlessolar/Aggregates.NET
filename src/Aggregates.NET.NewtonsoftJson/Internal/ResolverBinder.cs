using Aggregates.Contracts;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using System;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace Aggregates.Internal
{
    class EventContractResolver : DefaultContractResolver
    {
        private readonly IEventMapper _mapper;
        private readonly IEventFactory _factory;

        public EventContractResolver(IEventMapper mapper, IEventFactory factory)
        {
            _mapper = mapper;
            _factory = factory;
        }

        // https://stackoverflow.com/a/18548894/223547
        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            IList<JsonProperty> props = base.CreateProperties(type, memberSerialization);
            return props.Where(p => p.Writable).ToList();
        }
        // https://github.com/danielwertheim/jsonnet-privatesetterscontractresolvers/blob/master/src/JsonNet.PrivateSettersContractResolvers/PrivateSettersContractResolvers.cs
        // Need to be able to set private members because snapshots generally are { get; private set; } which won't deserialize properly
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var jProperty = base.CreateProperty(member, memberSerialization);
            if (jProperty.Writable)
                return jProperty;

            jProperty.Writable = isPropertyWithSetter(member);

            return jProperty;
        }
        private bool isPropertyWithSetter(MemberInfo member)
        {
            var property = member as PropertyInfo;

            return property?.GetSetMethod(true) != null;
        }
        protected override JsonObjectContract CreateObjectContract(Type objectType)
        {
            var mappedTypeFor = objectType;

            _mapper.Initialize(objectType);
            mappedTypeFor = _mapper.GetMappedTypeFor(objectType);

            if (mappedTypeFor == null)
                return base.CreateObjectContract(objectType);

            var objectContract = base.CreateObjectContract(objectType);

            objectContract.DefaultCreator = () => _factory.Create(objectType);

            return objectContract;
        }
    }

    class EventSerializationBinder : DefaultSerializationBinder
    {
        private readonly IEventMapper _mapper;

        public EventSerializationBinder(IEventMapper mapper)
        {
            _mapper = mapper;
        }

        public override void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            // Todo: this is where I would substitute the type info with a unique string to represent the object without namespaces
            var mappedType = serializedType;
            if (!serializedType.IsInterface)
                mappedType = _mapper.GetMappedTypeFor(serializedType) ?? serializedType;

            assemblyName = null;
            typeName = mappedType.AssemblyQualifiedName;
        }
    }
}
