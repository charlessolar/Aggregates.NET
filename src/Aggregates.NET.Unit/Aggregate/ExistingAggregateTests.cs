using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.MessageInterfaces;
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
        private Moq.Mock<IMessageCreator> _eventFactory;
        private Moq.Mock<IMessageMapper> _mapper;
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
            _mapper = new Moq.Mock<IMessageMapper>();

            _mapper.Setup(x => x.GetMappedTypeFor(typeof(CreatedEvent))).Returns(typeof(CreatedEvent));
            _mapper.Setup(x => x.GetMappedTypeFor(typeof(UpdatedEvent))).Returns(typeof(UpdatedEvent));
            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<CreatedEvent>>())).Returns<Action<CreatedEvent>>((e) => { var ev = new CreatedEvent(); e(ev); return ev; });
            _eventFactory.Setup(x => x.CreateInstance(Moq.It.IsAny<Action<UpdatedEvent>>())).Returns<Action<UpdatedEvent>>((e) => { var ev = new UpdatedEvent(); e(ev); return ev; });
            _eventFactory.Setup(x => x.CreateInstance(typeof(CreatedEvent))).Returns(new CreatedEvent());
            _eventFactory.Setup(x => x.CreateInstance(typeof(UpdatedEvent))).Returns(new UpdatedEvent());

            _store.Setup(x => x.GetSnapshot(Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>()));
            _store.Setup(x => x.GetStream(Moq.It.IsAny<String>(), Moq.It.IsAny<Int32>())).Returns(_stream.Object);
            _builder.Setup(x => x.CreateChildBuilder()).Returns(_builder.Object);
            _builder.Setup(x => x.Build<IRouteResolver>()).Returns(new Aggregates.Internal.DefaultRouteResolver(_mapper.Object));
            _builder.Setup(x => x.Build<IMessageCreator>()).Returns(_eventFactory.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_store.Object);
            _stream.Setup(x => x.StreamId).Returns(String.Format("{0}", _id));
            _stream.Setup(x => x.StreamVersion).Returns(0);
            _stream.Setup(x => x.Events).Returns(new List<Object> { new CreatedEvent { Value = "Test" } });

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, new DefaultRepositoryFactory());
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