using Aggregates.Contracts;
using NEventStore;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.Aggregate
{
    [TestFixture]
    public class ExistingAggregateTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IEventStream> _stream;
        private IUnitOfWork _uow;
        private Guid _id;

        [SetUp]
        public void Setup()
        {
            _id = Guid.NewGuid();
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
            _stream = new Moq.Mock<IEventStream>();

            _builder.Setup(x => x.Build<IRouteResolver>());
            _store.Setup(x => x.Advanced.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>()));
            _store.Setup(x => x.OpenStream("default", _id.ToString(), Int32.MinValue, Int32.MaxValue)).Returns(_stream.Object);
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _stream.Setup(x => x.CommittedEvents).Returns(new List<EventMessage> { new EventMessage { Body = new CreatedEvent { Value = "Test" } } });
            _stream.Setup(x => x.UncommittedEvents).Returns(new List<EventMessage>());

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _store.Object);
        }


        [Test]
        public void get_aggregate_with_events()
        {
            var root = _uow.For<_AggregateStub>().Get(_id);
            Assert.AreEqual(root.Value, "Test");
        }

        [Test]
        public void get_aggregate_update()
        {
            var root = _uow.For<_AggregateStub>().Get(_id);
            root.Update("updated");
            Assert.AreEqual(root.Value, "updated");
        }


    }
}
