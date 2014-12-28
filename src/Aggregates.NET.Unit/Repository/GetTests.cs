using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder.Common;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Repository
{
    [TestFixture]
    public class GetTests
    {
        private Moq.Mock<IContainer> _container;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IEventStream> _eventStream;
        private Moq.Mock<IEventRouter> _eventRouter;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<Aggregate<Guid>> _aggregate;
        private IRepository<Aggregate<Guid>> _repository;
        private Guid _id;

        [SetUp]
        public void Setup()
        {
            _id = Guid.NewGuid();
            _container = new Moq.Mock<IContainer>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _eventStream = new Moq.Mock<IEventStream>();
            _eventRouter = new Moq.Mock<IEventRouter>();
            _eventFactory = new Moq.Mock<IMessageCreator>();
            _eventStream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage>());
            _eventStream.Setup(x => x.UncommittedEvents).Returns(new List<EventMessage>());
            _eventStream.Setup(x => x.Dispose()).Verifiable();
            _eventStore.Setup(x => x.Advanced.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns((ISnapshot)null);
            _eventStore.Setup(x => x.OpenStream(Moq.It.IsAny<String>(), _id.ToString(), Moq.It.IsAny<Int32>(), Moq.It.IsAny<Int32>())).Returns(_eventStream.Object);
            _aggregate = new Moq.Mock<Aggregate<Guid>>();
            _aggregate.Setup(x => x.Container).Returns(_container.Object);
            _container.Setup(x => x.BuildChildContainer()).Returns(_container.Object);
            _container.Setup(x => x.Build(typeof(IEventRouter))).Returns(_eventRouter.Object);
            _container.Setup(x => x.Build(typeof(IMessageCreator))).Returns(_eventFactory.Object);
            _container.Setup(x => x.Build(typeof(Aggregate<Guid>))).Returns(_aggregate.Object);

            _repository = new Aggregates.Internal.Repository<Aggregate<Guid>>(_container.Object, _eventStore.Object);
        }

        [Test]
        public void get_existing_no_events()
        {
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id));
        }

        [Test]
        public void get_non_existing()
        {
            Assert.IsNull(_repository.Get(Guid.NewGuid()));
        }

        [Test]
        public void get_existing_with_events()
        {
            _eventStream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage> { new EventMessage { Body = "Test" } });
            _eventStream.Setup(x => x.UncommittedEvents).Returns(new List<EventMessage> { new EventMessage { Body = "Test" } });
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id));
        }

        [Test]
        public void get_existing_with_snapshot()
        {
            var snapshot = new Moq.Mock<ISnapshot>();
            _eventStore.Setup(x => x.Advanced.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns(snapshot.Object);
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id));
        }

        [Test]
        public void get_specific_version()
        {
            _eventStream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage> { new EventMessage { Body = "Test" }, new EventMessage { Body = "Test" }, new EventMessage { Body = "Test" } });
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id, 2));
        }

        [Test]
        public void get_cached_stream()
        {
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id));
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id));
        }

        [Test]
        public void get_cached_snapshot()
        {
            var snapshot = new Moq.Mock<ISnapshot>();
            _eventStore.Setup(x => x.Advanced.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns(snapshot.Object);
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id));
            Assert.IsInstanceOf<Aggregate<Guid>>(_repository.Get(_id));
        }
    }
}
