using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Attributes;
using Aggregates.Contracts;
using NServiceBus.Extensibility;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Common.Internal
{
    [TestFixture]
    public class BulkInvokeHandlerTerminator
    {
        [Delayed(typeof(DelayedMessage), count: 2, delayMs: 500)]
        [Delayed(typeof(DelayedMessageNoProps), count: 2)]
        class DelayedHandler { }
        
        class DelayedMessage
        {
            [KeyProperty]
            public int Test { get; set; }
        }
        class DelayedMessageNoProps { }

        private Moq.Mock<IMessageMapper> _mapper;
        private Moq.Mock<IDelayedMessage> _message;
        private Aggregates.Internal.BulkInvokeHandlerTerminator _terminator;

        [SetUp]
        public void Setup()
        {
            _mapper = new Moq.Mock<IMessageMapper>();
            _terminator = new Aggregates.Internal.BulkInvokeHandlerTerminator(_mapper.Object);
            Aggregates.Internal.BulkInvokeHandlerTerminator.RecentlyInvoked.Clear();

            _message = new Moq.Mock<IDelayedMessage>();
            _message.Setup(x => x.Message).Returns(new object());
        }

        [Test]
        public async Task no_problem_no_bulk()
        {
            var bag = new ContextBag();
            bool invoked = false;
            var handler = new MessageHandler((first, second, ctx) =>
            {
                invoked = true;
                return Task.CompletedTask;
            }, typeof(object));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageBeingHandled).Returns(new object());
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());

            await _terminator.Invoke(context.Object, next.Object);
            Assert.True(invoked);
        }

        [Test]
        public async Task not_delayed_not_checked()
        {
            var bag = new ContextBag();
            bool invoked = false;
            var handler = new MessageHandler((first, second, ctx) =>
            {
                invoked = true;
                return Task.CompletedTask;
            }, typeof(object));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var channel = new Moq.Mock<IDelayedChannel>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();
            builder.Setup(x => x.Build<IDelayedChannel>()).Returns(channel.Object);
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageBeingHandled).Returns(new object());
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());

            await _terminator.Invoke(context.Object, next.Object);
            Assert.True(invoked);
            channel.Verify(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Never);
            channel.Verify(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Never);
            channel.Verify(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()), Moq.Times.Never);
        }

        [Test]
        public async Task is_delayed_delayed()
        {
            var bag = new ContextBag();
            bool invoked = false;
            var handler = new MessageHandler((first, second, ctx) =>
            {
                invoked = true;
                return Task.CompletedTask;
            }, typeof(DelayedHandler));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var channel = new Moq.Mock<IDelayedChannel>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();

            builder.Setup(x => x.Build<IDelayedChannel>()).Returns(channel.Object);
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageBeingHandled).Returns(new DelayedMessage());
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());

            await _terminator.Invoke(context.Object, next.Object);
            Assert.False(invoked);
            channel.Verify(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            // Posibly optional
            channel.Verify(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            channel.Verify(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
        }
        
        [Test]
        public async Task is_delayed_with_key_props_channel_different()
        {

            var bag = new ContextBag();
            var handler = new MessageHandler((first, second, ctx) =>
            {
                return Task.CompletedTask;
            }, typeof(DelayedHandler));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var channel = new Moq.Mock<IDelayedChannel>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();

            builder.Setup(x => x.Build<IDelayedChannel>()).Returns(channel.Object);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            var channelKey = "";
            var keyPropChannelKey = "";
            

            context.Setup(x => x.MessageBeingHandled).Returns(new DelayedMessage { Test = 1 });
            channel.Setup(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()))
                .Callback<string, object, string>((chn, msg, key) => keyPropChannelKey = chn+key)
                .Returns(Task.CompletedTask);
            await _terminator.Invoke(context.Object, next.Object);
            context.Setup(x => x.MessageBeingHandled).Returns(new DelayedMessageNoProps());
            channel.Setup(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()))
                .Callback<string, object, string>((chn, msg, key) => channelKey = chn + key)
                .Returns(Task.CompletedTask);
            await _terminator.Invoke(context.Object, next.Object);

            Assert.IsNotEmpty(channelKey);
            Assert.IsNotEmpty(keyPropChannelKey);
            Assert.AreNotEqual(channelKey, keyPropChannelKey);
            
        }

        [Test]
        public async Task is_delayed_size_hit()
        {

            var bag = new ContextBag();
            int invoked = 0;
            var handler = new MessageHandler((first, second, ctx) =>
            {
                invoked++;
                return Task.CompletedTask;
            }, typeof(DelayedHandler));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var channel = new Moq.Mock<IDelayedChannel>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();

            builder.Setup(x => x.Build<IDelayedChannel>()).Returns(channel.Object);
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageBeingHandled).Returns(new DelayedMessage());
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            channel.Setup(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>())).Returns(Task.CompletedTask);
            channel.Setup(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>())).Returns(Task.FromResult(2));
            channel.Setup(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IDelayedMessage[] { _message.Object, _message.Object }.AsEnumerable()));

            await _terminator.Invoke(context.Object, next.Object);
            channel.Verify(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            // Posibly optional
            channel.Verify(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            channel.Verify(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            Assert.AreEqual(2, invoked);

        }

        [Test]
        public async Task is_delayed_age_hit()
        {

            var bag = new ContextBag();
            int invoked = 0;
            var handler = new MessageHandler((first, second, ctx) =>
            {
                invoked++;
                return Task.CompletedTask;
            }, typeof(DelayedHandler));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var channel = new Moq.Mock<IDelayedChannel>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();

            builder.Setup(x => x.Build<IDelayedChannel>()).Returns(channel.Object);
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageBeingHandled).Returns(new DelayedMessage());
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            channel.Setup(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>())).Returns(Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(2)));
            channel.Setup(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IDelayedMessage[] { _message.Object, _message.Object }.AsEnumerable()));

            await _terminator.Invoke(context.Object, next.Object);
            channel.Verify(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            // Posibly optional
            channel.Verify(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            channel.Verify(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            Assert.AreEqual(2, invoked);
        }
        

        [Test]
        public async Task is_delayed_hit_not_check_expired()
        {

            var bag = new ContextBag();
            int invoked = 0;
            var handler = new MessageHandler((first, second, ctx) =>
            {
                invoked++;
                return Task.CompletedTask;
            }, typeof(DelayedHandler));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var channel = new Moq.Mock<IDelayedChannel>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();

            builder.Setup(x => x.Build<IDelayedChannel>()).Returns(channel.Object);
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageBeingHandled).Returns(new DelayedMessage());
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            channel.Setup(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>())).Returns(Task.FromResult(2));
            channel.Setup(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IDelayedMessage[] { _message.Object, _message.Object }.AsEnumerable()));

            await _terminator.Invoke(context.Object, next.Object);
            channel.Verify(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            // Posibly optional
            channel.Verify(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            channel.Verify(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            Assert.AreEqual(2, invoked);

            channel.Verify(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), 1), Moq.Times.Never);
        }
        

        [Test]
        public async Task only_bulk_invoke_once()
        {

            var bag = new ContextBag();
            int invoked = 0;
            var handler = new MessageHandler((first, second, ctx) =>
            {
                invoked++;
                return Task.CompletedTask;
            }, typeof(DelayedHandler));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var channel = new Moq.Mock<IDelayedChannel>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();

            builder.Setup(x => x.Build<IDelayedChannel>()).Returns(channel.Object);
            
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageBeingHandled).Returns(new DelayedMessage());
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            channel.Setup(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>())).Returns(Task.FromResult(2));
            channel.Setup(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IDelayedMessage[] { _message.Object, _message.Object }.AsEnumerable()));

            await _terminator.Invoke(context.Object, next.Object);
            await _terminator.Invoke(context.Object, next.Object);
            channel.Verify(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()), Moq.Times.Exactly(2));
            Assert.AreEqual(2, invoked);
        }

        [Test]
        public async Task size_delayed_dont_check_expired()
        {

            var bag = new ContextBag();
            int invoked = 0;
            var handler = new MessageHandler((first, second, ctx) =>
            {
                invoked++;
                return Task.CompletedTask;
            }, typeof(DelayedHandler));
            var context = new Moq.Mock<IInvokeHandlerContext>();
            var builder = new Moq.Mock<IBuilder>();
            var channel = new Moq.Mock<IDelayedChannel>();
            var next = new Moq.Mock<Func<PipelineTerminator<IInvokeHandlerContext>.ITerminatingContext, Task>>();
            builder.Setup(x => x.Build<IDelayedChannel>()).Returns(channel.Object);
            context.Setup(x => x.MessageHandler).Returns(handler);
            context.Setup(x => x.Builder).Returns(builder.Object);
            context.Setup(x => x.Extensions).Returns(bag);
            context.Setup(x => x.MessageBeingHandled).Returns(new DelayedMessageNoProps());
            context.Setup(x => x.Headers).Returns(new Dictionary<string, string>());
            channel.Setup(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>())).Returns(Task.FromResult(1));
            

            channel.Setup(x => x.Pull("test", Moq.It.IsAny<string>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IDelayedMessage[] { new Aggregates.Internal.DelayedMessage(), new Aggregates.Internal.DelayedMessage() }.AsEnumerable()));

            await _terminator.Invoke(context.Object, next.Object);
            channel.Verify(x => x.AddToQueue(Moq.It.IsAny<string>(), Moq.It.IsAny<IDelayedMessage>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            // Posibly optional
            channel.Verify(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Never);
            channel.Verify(x => x.Size(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()), Moq.Times.Once);
            channel.Verify(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), 1), Moq.Times.Never);
            

        }
    }
}
