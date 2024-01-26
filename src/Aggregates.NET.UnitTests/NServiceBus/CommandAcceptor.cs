using Aggregates.Messages;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NServiceBus.Testing;
using System;
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

            await Sut.Invoke(context, next);

            A.CallTo(() => next()).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldReplyToCommand()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.ICommand>());
            context.MessageHeaders[Defaults.RequestResponse] = "1";
            context.ServiceCollection.TryAddSingleton(A.Fake<Action<Accept>>());

            await Sut.Invoke(context, next);

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
            context.ServiceCollection.TryAddSingleton(A.Fake<Action<BusinessException, Reject>>());

            var e = await Record.ExceptionAsync(() => Sut.Invoke(context, next));

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

            await Sut.Invoke(context, next);

            A.CallTo(() => next()).MustHaveHappened();
            context.RepliedMessages.Should().BeEmpty();
        }
        [Fact]
        public async Task ShouldStillRejectifNoResponseRequested()
        {
            var next = A.Fake<Func<Task>>();
            A.CallTo(() => next()).Throws<BusinessException>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<Messages.ICommand>());
            context.MessageHeaders[Defaults.RequestResponse] = "0";

            var e = await Record.ExceptionAsync(() => Sut.Invoke(context, next));

            e.Should().BeOfType<BusinessException>();
            A.CallTo(() => next()).MustHaveHappened();
            context.RepliedMessages.Should().BeEmpty();
        }
    }
}
