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
    public class AsyncronizedLoad
    {
        private Aggregates.Internal.AsyncronizedLoad _invoker;
        private Moq.Mock<IHandleMessagesAsync<string>> _handler;

        [SetUp]
        public void Setup()
        {
            var invoker = new Moq.Mock<IInvokeObjects>();
            _invoker = new Aggregates.Internal.AsyncronizedLoad(invoker.Object);
            _handler = new Moq.Mock<IHandleMessagesAsync<string>>();
        }

        [Test]
        public void normal()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            context.Setup(x => x.Get<bool>("NServiceBus.CallbackInvocationBehavior.CallbackWasInvoked")).Returns(false);
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(string));
            context.Setup(x => x.Builder.BuildAll(typeof(IHandleMessagesAsync<string>))).Returns(new IHandleMessagesAsync<string>[] { _handler.Object });

            Assert.DoesNotThrow(() => _invoker.Invoke(context.Object, next.Object));
            next.Verify(x => x(), Moq.Times.Once);
            context.Verify(x => x.Set(Moq.It.IsAny<AsyncMessageHandler>()), Moq.Times.Once);
        }
        [Test]
        public void callback_invoked()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            context.Setup(x => x.Get<bool>("NServiceBus.CallbackInvocationBehavior.CallbackWasInvoked")).Returns(true);
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(string));
            context.Setup(x => x.Builder.BuildAll(typeof(IHandleMessagesAsync<string>))).Returns(new IHandleMessagesAsync<string>[] { _handler.Object });

            Assert.DoesNotThrow(() => _invoker.Invoke(context.Object, next.Object));
            next.Verify(x => x(), Moq.Times.Once);
            context.Verify(x => x.Set(Moq.It.IsAny<AsyncMessageHandler>()), Moq.Times.Once);
        }
        [Test]
        public void callback_invoked_no_handler()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            context.Setup(x => x.Get<bool>("NServiceBus.CallbackInvocationBehavior.CallbackWasInvoked")).Returns(true);
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(string));
            context.Setup(x => x.Builder.BuildAll(typeof(IHandleMessagesAsync<string>))).Returns(new IHandleMessagesAsync<string>[] { });

            Assert.DoesNotThrow(() => _invoker.Invoke(context.Object, next.Object));
            next.Verify(x => x(), Moq.Times.Never);
        }
        [Test]
        public void no_handlers()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            context.Setup(x => x.Get<bool>("NServiceBus.CallbackInvocationBehavior.CallbackWasInvoked")).Returns(false);
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(string));
            context.Setup(x => x.Builder.BuildAll(typeof(IHandleMessagesAsync<string>))).Returns(new IHandleMessagesAsync<string>[] { });

            Assert.Throws<InvalidOperationException>(() => _invoker.Invoke(context.Object, next.Object));
        }
        [Test]
        public void second_handler()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            var secondHandler = new Moq.Mock<IHandleMessagesAsync<string>>();

            context.Setup(x => x.Get<bool>("NServiceBus.CallbackInvocationBehavior.CallbackWasInvoked")).Returns(false);
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(string));
            context.Setup(x => x.Builder.BuildAll(typeof(IHandleMessagesAsync<string>))).Returns(new IHandleMessagesAsync<string>[] { _handler.Object, secondHandler.Object });

            Assert.DoesNotThrow(() => _invoker.Invoke(context.Object, next.Object));
            next.Verify(x => x(), Moq.Times.Exactly(2));
            context.Verify(x => x.Set(Moq.It.IsAny<AsyncMessageHandler>()), Moq.Times.Exactly(2));
        }
        [Test]
        public void invokation_aborted()
        {
            var context = new Moq.Mock<IIncomingContextAccessor>();
            var next = new Moq.Mock<Action>();
            var secondHandler = new Moq.Mock<IHandleMessagesAsync<string>>();

            context.Setup(x => x.HandlerInvocationAborted).Returns(true);

            context.Setup(x => x.Get<bool>("NServiceBus.CallbackInvocationBehavior.CallbackWasInvoked")).Returns(false);
            context.Setup(x => x.IncomingLogicalMessageMessageType).Returns(typeof(string));
            context.Setup(x => x.Builder.BuildAll(typeof(IHandleMessagesAsync<string>))).Returns(new IHandleMessagesAsync<string>[] { _handler.Object, secondHandler.Object });

            Assert.DoesNotThrow(() => _invoker.Invoke(context.Object, next.Object));
            next.Verify(x => x(), Moq.Times.Once);
            context.Verify(x => x.Set(Moq.It.IsAny<AsyncMessageHandler>()), Moq.Times.Once);
        }
    }
}
