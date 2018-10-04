using Aggregates.Contracts;
using Aggregates.Messages;
using FakeItEasy;
using FluentAssertions;
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
    public class CommandAcceptor : TestSubject<Internal.CommandAcceptor>
    {
        [Fact]
        public async Task ShouldProcessEvent()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldReplyToCommand()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.ICommand>());
            context.MessageHeaders[Defaults.RequestResponse] = "1";
            context.Builder.Register<Action<Accept>>(A.Fake<Action<Accept>>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            context.RepliedMessages.Should().NotBeEmpty();
            context.RepliedMessages[0].Message.Should().BeAssignableTo<Accept>();
        }
        [Fact]
        public async Task ShouldRejectCommand()
        {
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<BusinessException>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.ICommand>());
            context.MessageHeaders[Defaults.RequestResponse] = "1";
            context.Builder.Register<Action<BusinessException, Reject>>(A.Fake<Action<BusinessException, Reject>>());

            var e = await Record.ExceptionAsync(() => Sut.Invoke(context, next)).ConfigureAwait(false);

            e.Should().BeOfType<BusinessException>();
            context.RepliedMessages.Should().NotBeEmpty();
            context.RepliedMessages[0].Message.Should().BeAssignableTo<Reject>();
        }
        [Fact]
        public async Task ShouldNotAcceptIfNoResponseRequested()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.ICommand>());
            context.MessageHeaders[Defaults.RequestResponse] = "0";

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            context.RepliedMessages.Should().BeEmpty();
        }
        [Fact]
        public async Task ShouldNotRejectifNoResponseRequested()
        {
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<BusinessException>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.ICommand>());
            context.MessageHeaders[Defaults.RequestResponse] = "0";

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            context.RepliedMessages.Should().BeEmpty();
        }
    }
}
