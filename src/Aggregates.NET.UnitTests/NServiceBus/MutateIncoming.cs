using Aggregates.Contracts;
using Aggregates.Messages;
using FakeItEasy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
    public class MutateIncoming : TestSubject<Internal.MutateIncoming>
    {
        [Fact]
        public async Task ShouldMutateMessage()
        {
            var mutator = new FakeMutator();
            var provider = Fake<IServiceProvider>();

            A.CallTo(() => provider.GetService(typeof(IEnumerable<Func<IMutate>>))).Returns(new Func<IMutate>[] { () => mutator });

            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.Extensions.Set<IServiceProvider>(provider);
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            mutator.MutatedIncoming.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldNotMutateReplies()
        {
            var mutator = new FakeMutator();
            Inject<IEnumerable<Func<IMutate>>>(new Func<IMutate>[] { () => mutator });

            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.MessageHeaders[Headers.MessageIntent] = MessageIntentEnum.Reply.ToString();
            context.UpdateMessageInstance(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            mutator.MutatedIncoming.Should().BeFalse();
        }
    }
}
