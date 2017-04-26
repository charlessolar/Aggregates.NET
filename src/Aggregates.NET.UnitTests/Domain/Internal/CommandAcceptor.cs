using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Messages;
using Aggregates.NET.UnitTests.Common.Internal;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class CommandAcceptor
    {
        class FakeCommand : ICommand { }

        class FakeReject : Reject
        {
            public BusinessException Exception { get; set; }
            public string Message { get; set; }
        }
        class FakeAccept : Accept { }

        private Aggregates.Internal.CommandAcceptor _acceptor;

        private ContextBag _bag;
        private Moq.Mock<IIncomingLogicalMessageContext> _context;
        private Moq.Mock<Func<Task>> _next;
        private Moq.Mock<IBuilder> _builder;

        [SetUp]
        public void Setup()
        {
            _acceptor = new Aggregates.Internal.CommandAcceptor();

            _bag = new ContextBag();
            _context = new Moq.Mock<IIncomingLogicalMessageContext>();
            _next = new Moq.Mock<Func<Task>>();
            _builder = new Moq.Mock<IBuilder>();

            _context.Setup(x => x.MessageId).Returns("1");
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(object)), new object()));
            _context.Setup(x => x.Extensions).Returns(_bag);
            _context.Setup(x => x.Builder).Returns(_builder.Object);
            _context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            _context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string> { [Headers.MessageIntent] = MessageIntentEnum.Send.ToString() });
            _builder.Setup(x => x.Build<Func<BusinessException, Reject>>()).Returns((e) => new FakeReject());
            _builder.Setup(x => x.Build<Func<Accept>>()).Returns(() => new FakeAccept());
        }

        [Test]
        public async Task no_problem()
        {
            await _acceptor.Invoke(_context.Object, _next.Object);
            _next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public async Task command_no_problem()
        {
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));

            await _acceptor.Invoke(_context.Object, _next.Object);
            _next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public void command_throws_no_response()
        {
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));
            _context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string> { [Headers.MessageIntent] = MessageIntentEnum.Send.ToString() });
            _context.Setup(x => x.Send(Moq.It.IsAny<object>(), Moq.It.IsAny<SendOptions>())).Returns(Task.CompletedTask); 

            _next.Setup(x => x()).Throws(new BusinessException("test"));

            Assert.DoesNotThrowAsync(() => _acceptor.Invoke(_context.Object, _next.Object));
            _next.Verify(x => x(), Moq.Times.Once);
            _context.Verify(x => x.Send(Moq.It.IsAny<object>(), Moq.It.IsAny<SendOptions>()), Moq.Times.Never);
        }
        [Test]
        public void command_throws_send_response()
        {
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));
            _context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string>
                {
                    [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                    [Defaults.RequestResponse] = "1"
                });
            _context.Setup(x => x.Send(Moq.It.IsAny<object>(), Moq.It.IsAny<SendOptions>())).Returns(Task.CompletedTask);

            _next.Setup(x => x()).Throws(new BusinessException("test"));

            Assert.DoesNotThrowAsync(() => _acceptor.Invoke(_context.Object, _next.Object));
            _builder.Verify(x => x.Build<Func<BusinessException, Reject>>(), Moq.Times.Once);
        }
        [Test]
        public void command_accept_send_response()
        {
            _context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));
            _context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string>
                {
                    [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                    [Defaults.RequestResponse] = "1"
                });
            _context.Setup(x => x.Send(Moq.It.IsAny<object>(), Moq.It.IsAny<SendOptions>())).Returns(Task.CompletedTask);

            _next.Setup(x => x()).Returns(Task.CompletedTask);

            Assert.DoesNotThrowAsync(() => _acceptor.Invoke(_context.Object, _next.Object));
            _next.Verify(x => x(), Moq.Times.Once);
            _builder.Verify(x => x.Build<Func<Accept>>(), Moq.Times.Once);
        }
    }
}
