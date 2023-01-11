using FakeItEasy;
using FluentAssertions;
using NServiceBus.Testing;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.NServiceBus
{
    public class SagaBehavior : TestSubject<Internal.SagaBehaviour>
    {
        [Fact]
        public async Task NoSagaHeader()
        {
            var message = "test";
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.UpdateMessageInstance(message);
            await Sut.Invoke(context, next);

            A.CallTo(() => next()).MustHaveHappened();
            context.Message.Instance.Should().BeOfType<string>().And.Be("test");
        }
        [Fact]
        public async Task AcceptReplaced()
        {
            var message = Fake<Messages.Accept>();
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.MessageHeaders.Add(Defaults.SagaHeader, "1");
            context.UpdateMessageInstance(message);

            await Sut.Invoke(context, next);

            A.CallTo(() => next()).MustHaveHappened();
            context.Message.Instance.Should().BeOfType<Sagas.ContinueCommandSaga>();
        }
        [Fact]
        public async Task RejectReplaced()
        {
            var message = Fake<Messages.Reject>();
            var next = A.Fake<Func<Task>>();
            var context = new TestableIncomingLogicalMessageContext();
            context.MessageHeaders.Add(Defaults.SagaHeader, "1");
            context.UpdateMessageInstance(message);

            await Sut.Invoke(context, next);

            A.CallTo(() => next()).MustHaveHappened();
            context.Message.Instance.Should().BeOfType<Sagas.AbortCommandSaga>();
        }
    }
}
