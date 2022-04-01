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
    public class FailureReply : TestSubject<Internal.FailureReply>
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
        public async Task MessageFailed()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.Headers.Add(NSBDefaults.FailedHeader, "1");
            context.MessageHeaders.Add(Headers.MessageIntent, MessageIntentEnum.Send.ToString());
            context.MessageHeaders.Add(Defaults.RequestResponse, "1");
            context.Builder.Register<Action<string, string, Error>>(Fake<Action<string, string, Error>>());

            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            context.RepliedMessages.Should().OnlyContain(x => x.Message is Error);
        }
        [Fact]
        public async Task MessageFailedAlreadyHandled()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.MessageHandled = true;
            context.Headers.Add(NSBDefaults.FailedHeader, "1");
            context.MessageHeaders.Add(Headers.MessageIntent, MessageIntentEnum.Send.ToString());
            context.MessageHeaders.Add(Defaults.RequestResponse, "1");
            context.Builder.Register<Action<string, string, Error>>(Fake<Action<string, string, Error>>());

            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            context.RepliedMessages.Should().BeEmpty();
        }
        [Fact]
        public async Task MessageFailedWasNotSend()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.Headers.Add(NSBDefaults.FailedHeader, "1");
            context.MessageHeaders.Add(Headers.MessageIntent, MessageIntentEnum.Reply.ToString());
            context.MessageHeaders.Add(Defaults.RequestResponse, "1");
            context.Builder.Register<Action<string, string, Error>>(Fake<Action<string, string, Error>>());

            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            context.RepliedMessages.Should().BeEmpty();
        }
        [Fact]
        public async Task MessageFailedNoReplyRequested()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.Headers.Add(NSBDefaults.FailedHeader, "1");
            context.MessageHeaders.Add(Headers.MessageIntent, MessageIntentEnum.Send.ToString());
            context.MessageHeaders.Add(Defaults.RequestResponse, "0");
            context.Builder.Register<Action<string, string, Error>>(Fake<Action<string, string, Error>>());

            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            context.RepliedMessages.Should().BeEmpty();
        }
    }
}
