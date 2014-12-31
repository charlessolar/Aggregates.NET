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

namespace Aggregates.Unit.UnitOfWork
{
    [TestFixture]
    public class DispatchTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;
        private IUnitOfWork _uow;
        private Moq.Mock<ICommit> _commit;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _bus = new Moq.Mock<IBus>();
            _commit = new Moq.Mock<ICommit>();
            _bus.Setup(x => x.Publish(Moq.It.IsAny<Object>())).Verifiable();
            _bus.Setup(x => x.OutgoingHeaders).Returns(new Dictionary<String, String>());

            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _eventStore.Object);
        }

        [Test]
        public void dispatch_no_events()
        {
            _commit.Setup(x => x.Events).Returns(() => new List<EventMessage>());
            _commit.Setup(x => x.Headers).Returns(() => new Dictionary<String, Object>());

            //Assert.DoesNotThrow(() => _uow.Dispatch(_commit.Object));
            _bus.Verify(x => x.Publish(Moq.It.IsAny<Object>()), Moq.Times.Never);
        }

        [Test]
        public void dispatch_has_headers()
        {
            _commit.Setup(x => x.Events).Returns(() => new List<EventMessage>());
            _commit.Setup(x => x.Headers).Returns(() => new Dictionary<String, Object> { { "Test", "Test" } });

            //Assert.DoesNotThrow(() => _uow.Dispatch(_commit.Object));
            //Assert.True(_uow.WorkHeaders.ContainsKey("Test"));
            //Assert.AreEqual(_uow.WorkHeaders["Test"], "Test");
            _bus.Verify(x => x.Publish(Moq.It.IsAny<Object>()), Moq.Times.Never);
        }

        [Test]
        public void dispatch_one()
        {
            _commit.Setup(x => x.Events).Returns(() => new List<EventMessage> { new EventMessage{ Body = "test" }});
            _commit.Setup(x => x.Headers).Returns(() => new Dictionary<String, Object>());
            //_uow.Dispatch(_commit.Object);
            _bus.Verify(x => x.Publish(Moq.It.IsAny<Object>()), Moq.Times.Once);
        }

        [Test]
        public void dispatch_one_event_header()
        {
            _commit.Setup(x => x.Events).Returns(() => new List<EventMessage> { new EventMessage { Body = "test", Headers = new Dictionary<string,object> {{"Test", "Test"}} } });
            _commit.Setup(x => x.Headers).Returns(() => new Dictionary<String, Object>());
            //_uow.Dispatch(_commit.Object);
            _bus.Verify(x => x.Publish(Moq.It.IsAny<Object>()), Moq.Times.Once);
        }


    }
}
