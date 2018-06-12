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
    public class LocalMessageUnpack : TestSubject<Internal.LocalMessageUnpack>
    {
        [Fact]
        public async Task ShouldUnpackBulkHeader()
        {
            var message = Fake<IFullMessage>();
            A.CallTo(() => message.Headers).Returns(new Dictionary<string, string> { [Defaults.ChannelKey] = "test" });
            Inject(message);
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<int>());
            context.Extensions.Set(Defaults.BulkHeader, Many<IFullMessage>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened(3, Times.Exactly);
        }
        [Fact]
        public async Task ShouldUnpackBulkMessage()
        {
            var message = new Internal.BulkMessage
            {
                Messages = Many<IFullMessage>()
            };
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(message);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened(3, Times.Exactly);
        }
        [Fact]
        public async Task ShouldUnpackRetriedBulkMessage()
        {
            var message = new Internal.BulkMessage
            {
                Messages = Many<IFullMessage>()
            };
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.Extensions.Set(Defaults.LocalHeader, message);
            context.UpdateMessageInstance(new Internal.BulkMessage());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened(3, Times.Exactly);
        }
        [Fact]
        public async Task ShouldUnpackLocalDelivery()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.Extensions.Set(Defaults.LocalHeader, Fake<IFullMessage>());
            context.UpdateMessageInstance(Fake<IFullMessage>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldProcessNormal()
        {
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(Fake<IFullMessage>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
        }
    }
}
