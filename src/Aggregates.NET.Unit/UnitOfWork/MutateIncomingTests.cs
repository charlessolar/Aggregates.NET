using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder.Common;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Unicast.Messages;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Unit.UnitOfWork
{
    [TestFixture]
    public class MutateIncomingTests
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
        public void incoming_has_headers()
        {
            var transportMessage = new TransportMessage("test", new Dictionary<String, String> { { "Test", "Test" } });

            _uow.MutateIncoming(transportMessage);

            Assert.True(_uow.WorkHeaders.ContainsKey("Test"));
            Assert.AreEqual(_uow.WorkHeaders["Test"], "Test");
        }

        [Test]
        public void incoming_multiple_headers()
        {
            var transportMessage1 = new TransportMessage("test", new Dictionary<String, String> { { "Test", "Test" } });
            var transportMessage2 = new TransportMessage("test", new Dictionary<String, String> { { "Test2", "Test" } });

            _uow.MutateIncoming(transportMessage1);
            _uow.MutateIncoming(transportMessage2);

            Assert.True(_uow.WorkHeaders.ContainsKey("Test"));
            Assert.AreEqual(_uow.WorkHeaders["Test"], "Test");
            Assert.True(_uow.WorkHeaders.ContainsKey("Test2"));
            Assert.AreEqual(_uow.WorkHeaders["Test2"], "Test");

        }

        [Test]
        public void incoming_no_headers()
        {
            var transportMessage = new TransportMessage();

            Assert.DoesNotThrow(() => _uow.MutateIncoming(transportMessage));
        }
    }
}
