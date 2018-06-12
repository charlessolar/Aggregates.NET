using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Messages;
using FakeItEasy;
using FluentAssertions;
using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.NServiceBus
{
    public class ExceptionRejector : TestSubject<Internal.ExceptionRejector>
    {
        [Fact]
        public async Task ShouldProcessMessage()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldQueueRetryOnException()
        {
            var retrier = A.Fake<Internal.DelayedRetry>();
            Inject(retrier);
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<Exception>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => retrier.QueueRetry(A<IFullMessage>.Ignored, A<TimeSpan>.Ignored)).MustHaveHappened();
            A.CallTo(() => next()).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldAddRetryHeader()
        {
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<Exception>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            context.Extensions.Get<int>(Defaults.Retries).Should().Be(0);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            context.Extensions.Get<int>(Defaults.Retries).Should().Be(1);
        }
        [Fact]
        public async Task ShouldNotQueueRetryOnBusinessException()
        {
            var retrier = A.Fake<Internal.DelayedRetry>();
            Inject(retrier);
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<BusinessException>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => retrier.QueueRetry(A<IFullMessage>.Ignored, A<TimeSpan>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => next()).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldReplyWithErrorAfterMaxRetries()
        {
            Configuration.Settings.Retries = 0;
            var retrier = A.Fake<Internal.DelayedRetry>();
            Inject(retrier);
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<Exception>();
            var context = new TestableIncomingLogicalMessageContext();
            context.MessageHeaders[Headers.MessageIntent] = MessageIntentEnum.Send.ToString();
            context.MessageHeaders[Defaults.RequestResponse] = "1";
            context.UpdateMessageInstance(Fake<Messages.IEvent>());
            context.Builder.Register<Func<Exception, string, Error>>(A.Fake<Func<Exception, string, Error>>());

            var e = await Record.ExceptionAsync(() => Sut.Invoke(context, next)).ConfigureAwait(false);

            e.Should().BeOfType<Exception>();
            A.CallTo(() => retrier.QueueRetry(A<IFullMessage>.Ignored, A<TimeSpan>.Ignored)).MustNotHaveHappened();
            context.RepliedMessages.Should().NotBeEmpty();
            context.RepliedMessages[0].Message.Should().BeAssignableTo<Messages.Error>();
        }
        [Fact]
        public async Task ShouldNotSendErrorReplyIfNotSendOrPublish()
        {
            Configuration.Settings.Retries = 0;
            var retrier = A.Fake<Internal.DelayedRetry>();
            Inject(retrier);
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<Exception>();
            var context = new TestableIncomingLogicalMessageContext();
            context.MessageHeaders[Headers.MessageIntent] = MessageIntentEnum.Reply.ToString();
            context.MessageHeaders[Defaults.RequestResponse] = "1";
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => retrier.QueueRetry(A<IFullMessage>.Ignored, A<TimeSpan>.Ignored)).MustNotHaveHappened();
            context.RepliedMessages.Should().BeEmpty();
        }
        [Fact]
        public async Task ShouldNotSendErrorReplyIfNoResponseRequested()
        {
            Configuration.Settings.Retries = 0;
            var retrier = A.Fake<Internal.DelayedRetry>();
            Inject(retrier);
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<Exception>();
            var context = new TestableIncomingLogicalMessageContext();
            context.MessageHeaders[Headers.MessageIntent] = MessageIntentEnum.Send.ToString();
            context.MessageHeaders[Defaults.RequestResponse] = "0";
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            var e = await Record.ExceptionAsync(() => Sut.Invoke(context, next)).ConfigureAwait(false);

            e.Should().BeOfType<Exception>();
            A.CallTo(() => retrier.QueueRetry(A<IFullMessage>.Ignored, A<TimeSpan>.Ignored)).MustNotHaveHappened();
            context.RepliedMessages.Should().BeEmpty();
        }
    }
}
