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
    public class SnapshottingAggregateTests
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

            var stub = new _AggregateStub(_container.Object);

            _container.Setup(x => x.Build(typeof(_AggregateStub))).Returns(stub);

            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<CreatedEvent>>())).Returns<Action<CreatedEvent>>((e) =>{ var ev = new CreatedEvent(); e(ev); return ev;});
            _eventStore.Setup(x => x.CreateStream(Moq.It.IsAny<String>(), Moq.It.IsAny<String>())).Returns(_eventStream.Object);
            _eventRouter.Setup(x => x.Get(typeof(CreatedEvent))).Returns((Action<Object>)(e => stub.Handle((CreatedEvent)e)));
            _repository = new Aggregates.Internal.Repository<_AggregateStub>(_container.Object, _eventStore.Object);
        }

        [Test]
        public void new_aggregate_take_snapshot()
        {
            var root = _repository.New(_id).Apply<CreatedEvent>(e => { e.Value = "Test"; });
            Assert.AreEqual(root.Version, 0);
        }
    }
}
