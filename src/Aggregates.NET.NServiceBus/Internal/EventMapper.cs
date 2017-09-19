using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;
using NServiceBus.MessageInterfaces;
using System.Threading.Tasks;
using System.Threading;

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
            while (!Bus.BusOnline)
                Thread.Sleep(100);

            return _mapper.GetMappedTypeFor(type);
        }
    }
}
