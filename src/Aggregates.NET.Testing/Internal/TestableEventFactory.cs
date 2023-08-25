using Aggregates.Contracts;
using NServiceBus;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    class TestableEventFactory : IEventFactory
    {
        private readonly IMessageCreator _creator;

        public TestableEventFactory(IMessageCreator creator)
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
