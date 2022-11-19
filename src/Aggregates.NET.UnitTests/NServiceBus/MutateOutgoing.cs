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
    public class MutateOutgoing : TestSubject<Internal.MutateOutgoing>
    {
        [Fact]
        public async Task ShouldMutateMessage()
        {
            var mutator = new FakeMutator();
            var provider = Fake<IServiceProvider>();

            A.CallTo(() => provider.GetService(typeof(IEnumerable<Func<IMutate>>))).Returns(new Func<IMutate>[] { () => mutator });

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingLogicalMessageContext();
            context.Extensions.Set<IServiceProvider>(provider);
            context.UpdateMessage(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            mutator.MutatedOutgoing.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldNotMutateReplies()
        {
            var mutator = new FakeMutator();
            var provider = Fake<IServiceProvider>();
            Inject<IEnumerable<Func<IMutate>>>(new Func<IMutate>[] { () => mutator });

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingLogicalMessageContext();
            context.Headers[Headers.MessageIntent] = MessageIntent.Reply.ToString();
            context.UpdateMessage(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            mutator.MutatedOutgoing.Should().BeFalse();
        }
    }
}
