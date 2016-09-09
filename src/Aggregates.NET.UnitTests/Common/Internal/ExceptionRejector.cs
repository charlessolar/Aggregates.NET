using Aggregates.Contracts;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Settings;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    [TestFixture]
    public class ExceptionRejector
    {
        private Aggregates.Internal.ExceptionRejector _rejector;
        private Moq.Mock<IBus> _bus;

        [SetUp]
        public void Setup()
        {
            _bus = new Moq.Mock<IBus>();
            var settings = new Moq.Mock<ReadOnlySettings>();
            settings.Setup(x => x.Get<Int32>("MaxRetries")).Returns(2);

            _rejector = new Aggregates.Internal.ExceptionRejector(_bus.Object, settings.Object);
        }

        [Test]
        public void no_problem()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            context.Setup(x => x.PhysicalMessageId).Returns("1");

            _rejector.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
        }
        [Test]
        public void is_retry_sets_headers()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            context.Setup(x => x.PhysicalMessageId).Returns("1");
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(int));
            context.Setup(x => x.PhysicalMessageBody).Returns(new byte[] { });
            next.Setup(x => x()).Throws(new Exception("test"));

            context.Setup(x => x.SetPhysicalMessageHeader(Headers.Retries, "1")).Verifiable();
            context.Setup(x => x.Set<Int32>("AggregatesNet.Retries", 1)).Verifiable();

            Assert.Throws<Exception>(() => _rejector.Invoke(context.Object, next.Object));

            next.Verify(x => x(), Moq.Times.Once);

            next.Setup(x => x());
            _rejector.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Exactly(2));

            context.Verify(x => x.SetPhysicalMessageHeader(Headers.Retries, "1"), Moq.Times.Once);
            context.Verify(x => x.Set<Int32>("AggregatesNet.Retries", 1), Moq.Times.Once);

        }
        [Test]
        public void max_retries()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            var errorFunc = new Moq.Mock<Func<Exception, String, Error>>();
            _bus.Setup(x => x.Send(Moq.It.IsAny<object>())).Verifiable();
            errorFunc.Setup(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>())).Returns(new Moq.Mock<Error>().Object).Verifiable();
            context.Setup(x => x.Builder.Build<Func<Exception, String, Error>>()).Returns(errorFunc.Object);

            context.Setup(x => x.PhysicalMessageId).Returns("1");
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(int));
            context.Setup(x => x.IncomingLogicalMessageInstance).Returns(new object());
            context.Setup(x => x.PhysicalMessageBody).Returns(new byte[] { });
            context.Setup(x => x.PhysicalMessageMessageIntent).Returns(MessageIntentEnum.Send);
            next.Setup(x => x()).Throws(new Exception("test"));

            context.Setup(x => x.SetPhysicalMessageHeader(Headers.Retries, "1")).Verifiable();
            context.Setup(x => x.Set<Int32>("AggregatesNet.Retries", 1)).Verifiable();

            Assert.Throws<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.Throws<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.DoesNotThrow(() => _rejector.Invoke(context.Object, next.Object));

            next.Verify(x => x(), Moq.Times.Exactly(3));

            context.Verify(x => x.SetPhysicalMessageHeader(Headers.Retries, "1"), Moq.Times.Once);
            context.Verify(x => x.Set<Int32>("AggregatesNet.Retries", 1), Moq.Times.Once);
            context.Verify(x => x.SetPhysicalMessageHeader(Headers.Retries, "2"), Moq.Times.Once);
            context.Verify(x => x.Set<Int32>("AggregatesNet.Retries", 2), Moq.Times.Once);
            context.Verify(x => x.SetPhysicalMessageHeader(Headers.Retries, "3"), Moq.Times.Never);
            context.Verify(x => x.Set<Int32>("AggregatesNet.Retries", 3), Moq.Times.Never);

            errorFunc.Verify(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            _bus.Verify(x => x.Reply(Moq.It.IsAny<object>()), Moq.Times.Once);
        }
        [Test]
        public void key_not_found_handled()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            var errorFunc = new Moq.Mock<Func<Exception, String, Error>>();
            _bus.Setup(x => x.Send(Moq.It.IsAny<object>())).Verifiable();
            errorFunc.Setup(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>())).Returns(new Moq.Mock<Error>().Object).Verifiable();
            context.Setup(x => x.Builder.Build<Func<Exception, String, Error>>()).Returns(errorFunc.Object);

            context.Setup(x => x.PhysicalMessageId).Returns("1");
            context.Setup(x => x.IncomingLogicalMessageInstance).Returns(new object());
            context.Setup(x => x.PhysicalMessageBody).Returns(new byte[] { });
            context.Setup(x => x.PhysicalMessageMessageIntent).Returns(MessageIntentEnum.Send);
            context.Setup(x => x.IncomingLogicalMessageMessageType).Throws(new KeyNotFoundException());

            next.Setup(x => x()).Throws(new Exception("test"));

            Assert.Throws<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.Throws<Exception>(() => _rejector.Invoke(context.Object, next.Object));
            Assert.DoesNotThrow(() => _rejector.Invoke(context.Object, next.Object));

            errorFunc.Verify(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            _bus.Verify(x => x.Reply(Moq.It.IsAny<object>()), Moq.Times.Once);

        }
    }
}
