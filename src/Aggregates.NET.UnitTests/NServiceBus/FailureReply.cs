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
    }
}
