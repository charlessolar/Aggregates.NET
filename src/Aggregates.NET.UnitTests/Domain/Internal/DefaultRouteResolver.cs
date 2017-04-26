using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using NServiceBus;
using NServiceBus.MessageInterfaces;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class DefaultRouteResolver
    {
        interface Test : IEvent { }
        interface Test2 :IEvent { }
        interface Test4 : IEvent { }

        class Entity : Aggregates.Aggregate<Entity>
        {
            private void Handle(Test e) { }
            private void Conflict(Test e) { }

            public void Handle(Test2 e) { }
            public void Conflict(Test2 e) { }

            private void HandleTheEvent(Test e) { }
            private void ConflictTheEvent(Test e) { }

            private void Handle(IEvent e) { }
            private void Conflict(IEvent e) { }

            private void Handle(Test e, IEvent e2) { }
            private void Conflict(Test e, IEvent e2) { }

            private Task Handle(Test4 e) { return Task.CompletedTask; }
            private Task Conflict(Test4 e) { return Task.CompletedTask; }
        }

        private Moq.Mock<IMessageMapper> _mapper;
        private Aggregates.Internal.DefaultRouteResolver _resolver;
        private Entity _entity;

        [SetUp]
        public void Setup()
        {
            _mapper = new Moq.Mock<IMessageMapper>();
            _resolver = new Aggregates.Internal.DefaultRouteResolver(_mapper.Object);
            _entity = new Entity();
        }

        [Test]
        public void test_event_resolved()
        {
            var action = _resolver.Resolve(_entity, typeof(Test));
            Assert.NotNull(action);
        }

        [Test]
        public void test_conflict_resolved()
        {
            var action = _resolver.Conflict(_entity, typeof(Test));
            Assert.NotNull(action);
        }

        [Test]
        public void public_route_event()
        {
            var action = _resolver.Resolve(_entity, typeof(Test2));
            Assert.Null(action);
        }
        [Test]
        public void public_route_conflict()
        {
            var action = _resolver.Conflict(_entity, typeof(Test2));
            Assert.Null(action);
        }

        [Test]
        public void incorrect_params_event()
        {
            var action = _resolver.Resolve(_entity, typeof(Test4));
            Assert.Null(action);
        }
        [Test]
        public void incorrect_params_conflict()
        {
            var action = _resolver.Conflict(_entity, typeof(Test4));
            Assert.Null(action);
        }
    }
}
