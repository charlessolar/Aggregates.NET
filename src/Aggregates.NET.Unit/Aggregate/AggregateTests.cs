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

namespace Aggregates.Unit.Aggregate
{
    [TestFixture]
    public class AggregateTests
    {
        private IRepository<_AggregateStub> _repository;
        private Moq.Mock<IContainer> _container;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IEventStream> _eventStream;
        private Moq.Mock<IEventRouter> _eventRouter;
        private Moq.Mock<IMessageCreator> _eventFactory;
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

            _container.Setup(x => x.BuildChildContainer()).Returns(_container.Object);
            _container.Setup(x => x.Build(typeof(IEventRouter))).Returns(_eventRouter.Object);
            _container.Setup(x => x.Build(typeof(IMessageCreator))).Returns(_eventFactory.Object);
            _container.Setup(x => x.Build(typeof(IEventStream))).Returns(_eventStream.Object);
            _eventStream.Setup(x => x.StreamId).Returns(String.Format("{0}", _id));
            _eventStream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage>());
            _eventStream.Setup(x => x.UncommittedEvents).Returns(new List<EventMessage>());

            var stub = new _AggregateStub(_container.Object);

            _container.Setup(x => x.Build(typeof(_AggregateStub))).Returns(stub);

            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<CreatedEvent>>())).Returns<Action<CreatedEvent>>((e) => { var ev = new CreatedEvent(); e(ev); return ev; });
            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<UpdatedEvent>>())).Returns<Action<UpdatedEvent>>((e) => { var ev = new UpdatedEvent(); e(ev); return ev; });
            _eventStore.Setup(x => x.CreateStream(Moq.It.IsAny<String>(), Moq.It.IsAny<String>())).Returns(_eventStream.Object);
            _eventStore.Setup(x => x.OpenStream(Moq.It.IsAny<String>(), _id.ToString(), Moq.It.IsAny<Int32>(), Moq.It.IsAny<Int32>())).Returns(_eventStream.Object);
            _eventStore.Setup(x => x.Advanced.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns((ISnapshot)null);
            _eventRouter.Setup(x => x.RouteFor(typeof(CreatedEvent))).Returns((Action<Object>)(e => stub.Handle((CreatedEvent)e)));
            _eventRouter.Setup(x => x.RouteFor(typeof(UpdatedEvent))).Returns((Action<Object>)(e => stub.Handle((UpdatedEvent)e)));
            _repository = new Aggregates.Internal.Repository<_AggregateStub>(_container.Object, _eventStore.Object);

        }

        [Test]
        public void new_aggregate_version_1()
        {
            var root = _repository.New(_id).Apply<CreatedEvent>(e => { e.Value = "Test"; });
            Assert.AreEqual(root.Version, 0);
        }

        [Test]
        public void new_aggregate_has_value_set()
        {
            var root = _repository.New(_id).Apply<CreatedEvent>(e => { e.Value = "Test"; });
            Assert.AreEqual(root.Value, "Test");
        }

        [Test]
        public void new_aggregate_throw_event()
        {
            var root = _repository.New(_id).Apply<CreatedEvent>(e => { e.Value = "Test"; });
            root.ThrowEvent("Updated");
            Assert.AreEqual(root.Value, "Updated");
        }

        [Test]
        public void new_aggregate_throw_event_same_version()
        {
            var root = _repository.New(_id).Apply<CreatedEvent>(e => { e.Value = "Test"; });
            root.ThrowEvent("Updated");
            Assert.AreEqual(root.Version, 0);
        }

        [Test]
        public void new_aggregate_with_bucket()
        {
            var root = _repository.New("test", _id).Apply<CreatedEvent>(e => { e.BucketId = "test"; e.Value = "Test"; });
            Assert.AreEqual(root.BucketId, "test");
        }

        [Test]
        public void new_aggregate_with_id()
        {
            var root = _repository.New("test", _id).Apply<CreatedEvent>(e => { e.Id = _id; e.Value = "Test"; });
            Assert.AreEqual(root.Id, _id);
        }

        [Test]
        public void get_aggreate_with_events()
        {
            _eventStream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage> { new EventMessage { Body = new CreatedEvent { Value = "Test" } } });

            var root = _repository.Get(_id);
            Assert.AreEqual(root.Value, "Test");
        }

    }
}
