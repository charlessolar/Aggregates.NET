using Aggregates.Contracts;
using Aggregates.Integrations;
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

namespace Aggregates.Unit.Dispatcher
{
    [TestFixture]
    public class DispatchTests
    {
        private Moq.Mock<IBus> _bus;
        private Moq.Mock<ICommit> _commit;
        private Aggregates.Integrations.Dispatcher _dispatcher;

        [SetUp]
        public void Setup()
        {
            _bus = new Moq.Mock<IBus>();
            _commit = new Moq.Mock<ICommit>();

            _bus.Setup(x => x.Publish(Moq.It.IsAny<Object>())).Verifiable();
            _bus.Setup(x => x.OutgoingHeaders).Returns(new Dictionary<String, String>());

            _dispatcher = new Aggregates.Integrations.Dispatcher(_bus.Object);
        }

        [Test]
        public void dispatch_no_events()
        {
            _commit.Setup(x => x.Events).Returns(() => new List<EventMessage>());
            _commit.Setup(x => x.Headers).Returns(() => new Dictionary<String, Object>());

            Assert.DoesNotThrow(() => _dispatcher.Dispatch(_commit.Object));
            _bus.Verify(x => x.Publish(Moq.It.IsAny<Object>()), Moq.Times.Never);
        }


        [Test]
        public void dispatch_one()
        {
            _commit.Setup(x => x.Events).Returns(() => new List<EventMessage> { new EventMessage{ Body = "test" }});
            _commit.Setup(x => x.Headers).Returns(() => new Dictionary<String, Object>());
            _dispatcher.Dispatch(_commit.Object);
            _bus.Verify(x => x.Publish(Moq.It.IsAny<Object>()), Moq.Times.Once);
        }

        [Test]
        public void dispatch_one_event_header()
        {
            _commit.Setup(x => x.Events).Returns(() => new List<EventMessage> { new EventMessage { Body = "test", Headers = new Dictionary<string,object> {{"Test", "Test"}} } });
            _commit.Setup(x => x.Headers).Returns(() => new Dictionary<String, Object>());
            _dispatcher.Dispatch(_commit.Object);
            _bus.Verify(x => x.Publish(Moq.It.IsAny<Object>()), Moq.Times.Once);
        }


    }
}
