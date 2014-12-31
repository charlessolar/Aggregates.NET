using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
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
    public class MutateOutgoingTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IBus> _bus;
        private IUnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _uow = new Aggregates.Internal.UnitOfWork(_builder.Object, _eventStore.Object);

        }


        [Test]
        public void outgoing_has_headers()
        {
            var transMsg = new TransportMessage("test", new Dictionary<string, string> { { "test", "test" } });
            _uow.MutateIncoming(transMsg);

            var transportMessage = new TransportMessage();
            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            Assert.True(transportMessage.Headers.ContainsKey("test"));
            Assert.AreEqual(transportMessage.Headers["test"], "test");
        }

        [Test]
        public void outgoing_multiple_mutates()
        {
            var transMsg = new TransportMessage("test", new Dictionary<string, string> { { "test", "test" } });
            _uow.MutateIncoming(transMsg);
            var transMsg2 = new TransportMessage("test", new Dictionary<string, string> { { "test2", "test2" } });
            _uow.MutateIncoming(transMsg2);

            var transportMessage = new TransportMessage();
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

        [Test]
        public void outgoing_carry_over()
        {
            var transMsg = new TransportMessage("test", new Dictionary<string, string> { { "NServiceBus.MessageId", "test" } });
            _uow.MutateIncoming(transMsg);

            var transportMessage = new TransportMessage();
            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            Assert.True(transportMessage.Headers.ContainsKey("Originating.NServiceBus.MessageId"));
            Assert.AreEqual(transportMessage.Headers["Originating.NServiceBus.MessageId"], "test");
        }

        [Test]
        public void outgoing_carry_over_not_found()
        {
            var transMsg = new TransportMessage("test", new Dictionary<string, string> { });
            _uow.MutateIncoming(transMsg);

            var transportMessage = new TransportMessage();
            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            Assert.True(transportMessage.Headers.ContainsKey("Originating.NServiceBus.MessageId"));
            Assert.AreEqual(transportMessage.Headers["Originating.NServiceBus.MessageId"], "<NOT FOUND>");
        }

        [Test]
        public void outgoing_filter_headers()
        {
            var transMsg = new TransportMessage("test", new Dictionary<string, string> { { "$.diag", "" }, { "WinIdName", "" }, { "NServiceBus", "" }, { "CorrId", "" } });
            _uow.MutateIncoming(transMsg);

            var transportMessage = new TransportMessage();
            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            Assert.False(transportMessage.Headers.ContainsKey("$.diag"));
            Assert.False(transportMessage.Headers.ContainsKey("WinIdName"));
            Assert.False(transportMessage.Headers.ContainsKey("NServiceBus"));
            Assert.False(transportMessage.Headers.ContainsKey("CorrId"));
        }
    }
}
