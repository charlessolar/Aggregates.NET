using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;
using NServiceBus.MessageInterfaces;

namespace Aggregates.Internal
{
    public class EventMapper : IEventMapper
    {
        private readonly IMessageMapper _mapper;

        public EventMapper(IMessageMapper mapper)
        {
            _mapper = mapper;
        }

        public Type GetMappedTypeFor(Type type)
        {
            return _mapper.GetMappedTypeFor(type);
        }
    }
}
