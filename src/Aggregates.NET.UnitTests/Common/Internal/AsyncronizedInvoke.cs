using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.MessageInterfaces;
using NServiceBus.Settings;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    [TestFixture]
    public class AsyncronizedInvoke
    {
        private Aggregates.Internal.AsyncronizedInvoke _invoker;

        [SetUp]
        public void Setup()
        {
            var bus = new Moq.Mock<IBus>();
            var settings = new Moq.Mock<ReadOnlySettings>();
            var mapper = new Moq.Mock<IMessageMapper>();
            settings.Setup(x => x.Get<Int32>("SlowAlertThreshold")).Returns(500);

            _invoker = new Aggregates.Internal.AsyncronizedInvoke(bus.Object, settings.Object, mapper.Object);
        }

        [Test]
        public void normal_invoke()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            var handler = new Moq.Mock<IHandleMessagesAsync<object>>();
            var invoke = new Moq.Mock<Func<dynamic, object, IHandleContext, Task>>();
            var wrapper = new AsyncMessageHandler
            {
                Handler = handler.Object,
                Invocation = invoke.Object
            };
            context.Setup(x => x.PhysicalMessageId).Returns("1");
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(int));
            context.Setup(x => x.IncomingLogicalMessageInstance).Returns(new object());
            context.Setup(x => x.PhysicalMessageBody).Returns(new byte[] { });

            context.Setup(x => x.Get<AsyncMessageHandler>()).Returns(wrapper);

            _invoker.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
            invoke.Verify(x => x(Moq.It.IsAny<object>(), Moq.It.IsAny<object>(), Moq.It.IsAny<IHandleContext>()), Moq.Times.Once);
        }
        [Test]
        public void slow_invoke()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            var handler = new Moq.Mock<IHandleMessagesAsync<object>>();
            var wrapper = new AsyncMessageHandler
            {
                Handler = handler.Object,
                Invocation = (h, obj, ctx) => { Thread.Sleep(500); return Task.CompletedTask; }
            };
            context.Setup(x => x.PhysicalMessageId).Returns("1");
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(int));
            context.Setup(x => x.IncomingLogicalMessageInstance).Returns(new object());
            context.Setup(x => x.PhysicalMessageBody).Returns(new byte[] { });
            
            context.Setup(x => x.Get<AsyncMessageHandler>()).Returns(wrapper);

            _invoker.Invoke(context.Object, next.Object);
            next.Verify(x => x(), Moq.Times.Once);
        }
    }
}
