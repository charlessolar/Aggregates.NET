using Aggregates.Contracts;
using Aggregates.Internal;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder;
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
    public class NewAggregateTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IMessageCreator> _eventFactory;
        private IUnitOfWork _uow;
        private Guid _id;

        [SetUp]
        public void Setup()
        {
            _id = Guid.NewGuid();
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
            _stream = new Moq.Mock<IEventStream>();
            _eventFactory = new Moq.Mock<IMessageCreator>();

            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<CreatedEvent>>())).Returns<Action<CreatedEvent>>((e) => { var ev = new CreatedEvent(); e(ev); return ev; });
            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<UpdatedEvent>>())).Returns<Action<UpdatedEvent>>((e) => { var ev = new UpdatedEvent(); e(ev); return ev; });
            _eventFactory.Setup(x => x.CreateInstance(typeof(CreatedEvent))).Returns(new CreatedEvent());
            _eventFactory.Setup(x => x.CreateInstance(typeof(UpdatedEvent))).Returns(new UpdatedEvent());

            _store.Setup(x => x.Advanced.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>()));
            _store.Setup(x => x.CreateStream(Moq.It.IsAny<String>(), _id.ToString())).Returns(_stream.Object);
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IRouteResolver>()).Returns(new Aggregates.Internal.DefaultRouteResolver());
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _stream.Setup(x => x.StreamId).Returns(String.Format("{0}", _id));
            _stream.Setup(x => x.StreamRevision).Returns(0);
            _stream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage>());
            _stream.Setup(x => x.UncommittedEvents).Returns(new List<EventMessage>());

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _store.Object, new DefaultRepositoryFactory());
        }

        [Test]
        public void new_aggregate_stream_version()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            Assert.AreEqual(root.StreamId, _id.ToString());
        }

        [Test]
        public void new_aggregate_version_0()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            root.Create(_id, "test");
            Assert.AreEqual(root.Version, 0);
        }

        [Test]
        public void new_aggregate_has_value_set()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            root.Create(_id, "test");
            Assert.AreEqual(root.Value, "test");
        }

        [Test]
        public void new_aggregate_throw_event()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            root.Create(_id, "test");
            root.Update("Updated");
            Assert.AreEqual(root.Value, "Updated");
        }

        [Test]
        public void new_aggregate_throw_event_same_version()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            root.Create(_id, "test");
            root.Update("Updated");
            Assert.AreEqual(root.Version, 0);
        }

        [Test]
        public void new_aggregate_with_bucket()
        {
            //_stream.Setup(x => x.BucketId).Returns("test");
            var root = _uow.For<_AggregateStub>().New("test", _id);
            root.Create(_id, "test");
            
            Assert.AreEqual(root.BucketId, "test");
        }

        [Test]
        public void new_aggregate_with_id()
        {
            var root = _uow.For<_AggregateStub>().New(_id);
            Assert.AreEqual(root.Id, _id);
        }


    }
}
