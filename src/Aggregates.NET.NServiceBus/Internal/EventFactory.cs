using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Contracts;
using NServiceBus;

namespace Aggregates.Internal
{
    public class EventFactory : IEventFactory
    {
        private readonly IMessageCreator _creator;

        public EventFactory(IMessageCreator creator)
        {
            _creator = creator;
        }

        public T Create<T>(Action<T> action)
        {
            return _creator.CreateInstance(action);
        }

        public object Create(Type type)
        {
            return _creator.CreateInstance(type);
        }
    }
}
