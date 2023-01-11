using Aggregates.Contracts;
using NServiceBus.MessageInterfaces;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    public class EventMapper : IEventMapper
    {
        private readonly IMessageMapper _mapper;

        public EventMapper(IMessageMapper mapper)
        {
            _mapper = mapper;
        }

        public void Initialize(Type type)
        {
            _mapper.Initialize(new[] { type });
        }

        public Type GetMappedTypeFor(Type type)
        {
            return _mapper.GetMappedTypeFor(type);
        }
    }
}
