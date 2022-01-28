using Aggregates.Contracts;
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
    [CollectionDefinition("Non-Parallel Collection", DisableParallelization = true)]
    public class MutateOutgoing : TestSubject<Internal.MutateOutgoing>
    {
        [Fact]
        public async Task ShouldMutateMessage()
        {
            var mutator = new FakeMutator();
            var settings = Fake<Configure>();
            A.CallTo(() => settings.Container.Resolve(A<Type>.Ignored)).Returns(mutator);
            MutationManager.RegisterMutator("test", typeof(FakeMutator));

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingLogicalMessageContext();
            context.UpdateMessage(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            mutator.MutatedOutgoing.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldMutateMessageWithLocalContainer()
        {
            var mutator = new FakeMutator();
            MutationManager.RegisterMutator("test", typeof(FakeMutator));
            var container = Fake<IContainer>();
            A.CallTo(() => container.Resolve(A<Type>.Ignored)).Returns(mutator);

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingLogicalMessageContext();
            context.UpdateMessage(Fake<Messages.IEvent>());
            context.Extensions.Set(container);

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            mutator.MutatedOutgoing.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldNotMutateReplies()
        {
            var mutator = new FakeMutator();
            var settings = Fake<Configure>();
            A.CallTo(() => settings.Container.Resolve(A<Type>.Ignored)).Returns(mutator);
            MutationManager.RegisterMutator("test", typeof(FakeMutator));

            var next = A.Fake<Func<Task>>();
            var context = new TestableOutgoingLogicalMessageContext();
            context.Headers[Headers.MessageIntent] = MessageIntentEnum.Reply.ToString();
            context.UpdateMessage(Fake<Messages.IEvent>());

            await Sut.Invoke(context, next).ConfigureAwait(false);

            A.CallTo(() => next()).MustHaveHappened();
            mutator.MutatedOutgoing.Should().BeFalse();
        }
    }
}
