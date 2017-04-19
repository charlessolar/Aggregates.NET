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

        [SetUp]
        public void Setup()
        {
            _acceptor = new Aggregates.Internal.CommandAcceptor();
        }

        [Test]
        public async Task no_problem()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var builder = new Moq.Mock<IBuilder>();
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(object)), new object()));
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string> { [Headers.MessageIntent] = MessageIntentEnum.Send.ToString() });

            await _acceptor.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public async Task command_no_problem()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var builder = new Moq.Mock<IBuilder>();
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string> { [Headers.MessageIntent] = MessageIntentEnum.Send.ToString() });

            await _acceptor.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public void command_throws_no_response()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var builder = new Moq.Mock<IBuilder>();
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string> { [Headers.MessageIntent] = MessageIntentEnum.Send.ToString() });
            context.Setup(x => x.Send(Moq.It.IsAny<object>(), Moq.It.IsAny<SendOptions>())).Returns(Task.CompletedTask); 

            next.Setup(x => x()).Throws(new BusinessException("test"));

            Assert.DoesNotThrowAsync(() => _acceptor.Invoke(context.Object, next.Object));
            next.Verify(x => x(), Moq.Times.Once);
            context.Verify(x => x.Send(Moq.It.IsAny<object>(), Moq.It.IsAny<SendOptions>()), Moq.Times.Never);
        }
        [Test]
        public void command_throws_send_response()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var builder = new Moq.Mock<IBuilder>();
            builder.Setup(x => x.Build<Func<BusinessException, Reject>>()).Returns((e) => new FakeReject());
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string>
                {
                    [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                    [Defaults.RequestResponse] = "1"
                });
            context.Setup(x => x.Send(Moq.It.IsAny<object>(), Moq.It.IsAny<SendOptions>())).Returns(Task.CompletedTask);

            next.Setup(x => x()).Throws(new BusinessException("test"));

            Assert.DoesNotThrowAsync(() => _acceptor.Invoke(context.Object, next.Object));
            builder.Verify(x => x.Build<Func<BusinessException, Reject>>(), Moq.Times.Once);
        }
        [Test]
        public void command_accept_send_response()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingLogicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var builder = new Moq.Mock<IBuilder>();
            builder.Setup(x => x.Build<Func<Accept>>()).Returns(() => new FakeAccept());
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Message).Returns(new LogicalMessage(new NServiceBus.Unicast.Messages.MessageMetadata(typeof(FakeCommand)), new FakeCommand()));
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            context.Setup(x => x.MessageHeaders)
                .Returns(new Dictionary<string, string>
                {
                    [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                    [Defaults.RequestResponse] = "1"
                });
            context.Setup(x => x.Send(Moq.It.IsAny<object>(), Moq.It.IsAny<SendOptions>())).Returns(Task.CompletedTask);

            next.Setup(x => x()).Returns(Task.CompletedTask);

            Assert.DoesNotThrowAsync(() => _acceptor.Invoke(context.Object, next.Object));
            next.Verify(x => x(), Moq.Times.Once);
            builder.Verify(x => x.Build<Func<Accept>>(), Moq.Times.Once);
        }
    }
}
