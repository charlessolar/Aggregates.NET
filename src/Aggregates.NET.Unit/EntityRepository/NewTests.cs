using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.EntityRepository
{
    [TestFixture]
    public class NewTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<IRouteResolver> _router;
        private Aggregates.Internal.EntityRepository<Guid, _EntityStub> _repository;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _stream = new Moq.Mock<IEventStream>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _router = new Moq.Mock<IRouteResolver>();
            _stream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage>());
            _stream.Setup(x => x.UncommittedEvents).Returns(new List<EventMessage>());
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _builder.Setup(x => x.Build<IRouteResolver>()).Returns(_router.Object);

            _repository = new Internal.EntityRepository<Guid, _EntityStub>(Guid.NewGuid(), _builder.Object, _stream.Object);
        }

        [Test]
        public void new_not_null()
        {
            var entity = _repository.New(Guid.NewGuid());
            Assert.NotNull(entity);
        }

        [Test]
        public void new_adds_event()
        {
            _stream.Setup(x => x.Add(Moq.It.IsAny<EventMessage>())).Verifiable();
            var entity = _repository.New(Guid.NewGuid());
            _stream.Verify(x => x.Add(Moq.It.IsAny<EventMessage>()), Moq.Times.Once);
        }
    }
}