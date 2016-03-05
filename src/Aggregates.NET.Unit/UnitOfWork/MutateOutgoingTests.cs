using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.Unicast.Messages;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Aggregates.Unit.UnitOfWork
{
    [TestFixture]
    public class MutateOutgoingTests
    {
        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _eventStore;
        private Moq.Mock<IRepositoryFactory> _repoFactory;
        private Moq.Mock<IProcessor> _processor;
        private Aggregates.Internal.UnitOfWork _uow;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _eventStore = new Moq.Mock<IStoreEvents>();
            _repoFactory = new Moq.Mock<IRepositoryFactory>();
            _processor = new Moq.Mock<IProcessor>();
            _builder.Setup(x => x.Build<IProcessor>()).Returns(_processor.Object);

            _uow = new Aggregates.Internal.UnitOfWork(_repoFactory.Object);
            _uow.Builder = _builder.Object;
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
            Assert.AreEqual(transportMessage.Headers["test2"], "test2");
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
        public void outgoing_message_id()
        {
            var transMsg = new TransportMessage("test", new Dictionary<string, string> { });
            _uow.MutateIncoming(transMsg);

            var transportMessage = new TransportMessage();
            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            var messageIdHeader = String.Format("{0}.NServiceBus.MessageId", Aggregates.Internal.UnitOfWork.PrefixHeader);

            Assert.True(transportMessage.Headers.ContainsKey(messageIdHeader));
            Assert.AreEqual(transportMessage.Headers[messageIdHeader], "test");
        }

        [Test]
        public void outgoing_carry_over_not_found()
        {
            var transMsg = new TransportMessage("", new Dictionary<string, string> { });
            _uow.MutateIncoming(transMsg);

            var transportMessage = new TransportMessage();
            _uow.MutateOutgoing(Moq.It.IsAny<LogicalMessage>(), transportMessage);

            foreach (var carryOver in Defaults.CarryOverHeaders)
            {
                var header = String.Format("{0}.{1}", Aggregates.Internal.UnitOfWork.PrefixHeader, carryOver);

                Assert.True(transportMessage.Headers.ContainsKey(header));
                Assert.AreEqual(transportMessage.Headers[header], Aggregates.Internal.UnitOfWork.NotFound);
            }
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