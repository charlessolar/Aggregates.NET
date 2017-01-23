using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Pipeline;
using NServiceBus.Settings;
using NServiceBus.Transport;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    [TestFixture]
    public class ExceptionRejector
    {
        private Aggregates.Internal.ExceptionRejector _rejector;

        [SetUp]
        public void Setup()
        {
            _rejector = new Aggregates.Internal.ExceptionRejector(2);
        }

        [Test]
        public async Task no_problem()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingPhysicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Extensions).Returns(bag);

            await _rejector.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public async Task sets_context_bag()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingPhysicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            context.Setup(x => x.MessageId).Returns("1");
            context.Setup(x => x.Message).Returns(new IncomingMessage("1", new Dictionary<string, string>(), new byte[] { }));
            context.Setup(x => x.Extensions).Returns(bag);

            next.Setup(x => x()).Throws(new Exception("test"));
            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            next.Verify(x => x(), Moq.Times.Once);
            int retries;
            Assert.True(bag.TryGet<int>(Defaults.Retries, out retries));
            Assert.AreEqual(0, retries);

            next.Setup(x => x()).Returns(Task.CompletedTask);
            await _rejector.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Exactly(2));

            Assert.True(bag.TryGet<int>(Defaults.Retries, out retries));
            Assert.AreEqual(1, retries);
        }
        [Test]
        public Task max_retries_no_response_needed()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingPhysicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var errorFunc = new Moq.Mock<Func<Exception, String, Error>>();
            errorFunc.Setup(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>())).Returns(new Moq.Mock<Error>().Object).Verifiable();
            context.Setup(x => x.Builder.Build<Func<Exception, String, Error>>()).Returns(errorFunc.Object);

            context.Setup(x => x.MessageId).Returns("2");
            context.Setup(x => x.Message).Returns(new IncomingMessage("2", new Dictionary<string, string>()
            {
                [Headers.MessageIntent]= MessageIntentEnum.Send.ToString(),
                [Defaults.RequestResponse]="0"
            }, new byte[] { }));
            context.Setup(x => x.Extensions).Returns(bag);
            next.Setup(x => x()).Throws(new Exception("test"));


            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));

            int retries;
            Assert.True(bag.TryGet<int>(Defaults.Retries, out retries));
            Assert.AreEqual(2, retries);

            next.Verify(x => x(), Moq.Times.Exactly(3));
            
            errorFunc.Verify(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>()), Moq.Times.Never);

            return Task.CompletedTask;
        }
        [Test]
        public Task max_retries_response_needed()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingPhysicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var errorFunc = new Moq.Mock<Func<Exception, String, Error>>();
            errorFunc.Setup(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>())).Returns(new Moq.Mock<Error>().Object).Verifiable();
            context.Setup(x => x.Builder.Build<Func<Exception, String, Error>>()).Returns(errorFunc.Object);

            context.Setup(x => x.MessageId).Returns("2");
            context.Setup(x => x.Message).Returns(new IncomingMessage("2", new Dictionary<string, string>()
            {
                [Headers.MessageIntent] = MessageIntentEnum.Send.ToString(),
                [Defaults.RequestResponse] = "1"
            }, new byte[] { }));
            context.Setup(x => x.Extensions).Returns(bag);
            next.Setup(x => x()).Throws(new Exception("test"));


            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));

            int retries;
            Assert.True(bag.TryGet<int>(Defaults.Retries, out retries));
            Assert.AreEqual(2, retries);

            next.Verify(x => x(), Moq.Times.Exactly(3));

            errorFunc.Verify(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>()), Moq.Times.Once);

            return Task.CompletedTask;
        }
        [Test]
        public Task fail_a_non_send()
        {
            var bag = new ContextBag();
            var context = new Moq.Mock<IIncomingPhysicalMessageContext>();
            var next = new Moq.Mock<Func<Task>>();
            var errorFunc = new Moq.Mock<Func<Exception, String, Error>>();
            errorFunc.Setup(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>())).Returns(new Moq.Mock<Error>().Object).Verifiable();
            context.Setup(x => x.Builder.Build<Func<Exception, String, Error>>()).Returns(errorFunc.Object);

            context.Setup(x => x.MessageId).Returns("3");
            context.Setup(x => x.Message).Returns(new IncomingMessage("3", new Dictionary<string, string>()
            {
                [Headers.MessageIntent] = MessageIntentEnum.Reply.ToString(),
            }, new byte[] { }));
            context.Setup(x => x.Extensions).Returns(bag);
            next.Setup(x => x()).Throws(new Exception("test"));


            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.ThrowsAsync<Exception>(() => _rejector.Invoke(context.Object, next.Object));

            int retries;
            Assert.True(bag.TryGet<int>(Defaults.Retries, out retries));
            Assert.AreEqual(2, retries);

            next.Verify(x => x(), Moq.Times.Exactly(3));

            errorFunc.Verify(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>()), Moq.Times.Never);

            return Task.CompletedTask;
        }
    }
}
