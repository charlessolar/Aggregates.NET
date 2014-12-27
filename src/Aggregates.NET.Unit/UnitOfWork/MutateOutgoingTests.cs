using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder.Common;
using NServiceBus.Unicast.Messages;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NET.Unit.UnitOfWork
{
    [TestFixture]
    public class MutateOutgoingTests
    {
        private Moq.Mock<IContainer> _container;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _container = new Moq.Mock<IContainer>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _bus = new Moq.Mock<IBus>();
            _uow = new Aggregates.Internal.UnitOfWork(_container.Object, _eventStore.Object, _bus.Object);
        }


        [Test]
        public void outgoing_has_headers()
        {
            _uow.WorkHeaders["test"] = "test";

            var transportMessage = new TransportMessage();

            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            Assert.True(transportMessage.Headers.ContainsKey("test"));
            Assert.AreEqual(transportMessage.Headers["test"], "test");
        }

        [Test]
        public void outgoing_multiple_mutates()
        {
            _uow.WorkHeaders["test"] = "test";

            var transportMessage = new TransportMessage();
            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            _uow.WorkHeaders["test2"] = "test";
            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            Assert.True(transportMessage.Headers.ContainsKey("test"));
            Assert.AreEqual(transportMessage.Headers["test"], "test");
            Assert.True(transportMessage.Headers.ContainsKey("test2"));
            Assert.AreEqual(transportMessage.Headers["test2"], "test");
        }

        [Test]
        public void outgoing_no_headers()
        {
            var transportMessage = new TransportMessage();

            Assert.DoesNotThrow(() => _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage));
        }
    }
}
