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
    public class GetTests
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
        public void get_doesnt_exist()
        {
            var entity = _repository.Get(Guid.NewGuid());
            Assert.IsNull(entity);
        }

        [Test]
        public void get_exists()
        {
            var id = Guid.NewGuid();
            _stream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage> { new EventMessage { Headers = new Dictionary<string, object> { { "Id", id } }, Body = null } });
            var entity = _repository.Get(id);
            Assert.NotNull(entity);
        }

        [Test]
        public void get_invalid_stream()
        {
            var id = Guid.NewGuid();
            _stream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage> { new EventMessage { Headers = new Dictionary<string, object> { { "Test", "test" } }, Body = null } });
            var entity = _repository.Get(id);
            Assert.Null(entity);
        }

        [Test]
        public void get_valid_and_invalid_stream()
        {
            var id = Guid.NewGuid();
            _stream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage> {
                new EventMessage { Headers = new Dictionary<string, object> { { "Id", id } }, Body = null },
                new EventMessage { Headers = new Dictionary<string, object> { { "test", "test" } }, Body = null }
            });
            var entity = _repository.Get(id);
            Assert.NotNull(entity);
        }

        [Test]
        public void get_with_other_ids()
        {
            var id = Guid.NewGuid();
            _stream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage> {
                new EventMessage { Headers = new Dictionary<string, object> { { "Id", id } }, Body = null },
                new EventMessage { Headers = new Dictionary<string, object> { { "Id", Guid.NewGuid() } }, Body = null }
            });
            var entity = _repository.Get(id);
            Assert.NotNull(entity);
        }
    }
}