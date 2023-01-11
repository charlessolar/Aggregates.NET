using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Aggregates.NServiceBus
{
    public class NSBUnitOfWork : TestSubject<Internal.NSBUnitOfWork>
    {
        [Fact]
        public void ShouldPopulateCurrentMessageAndHeaders()
        {
            var mutating = Fake<IMutating>();
            A.CallTo(() => mutating.Message).Returns(1);

            Sut.MutateIncoming(mutating);

            Sut.CurrentMessage.Should().Be(1);
        }
        [Fact]
        public void ShouldWriteOutgoingHeaders()
        {

        }
        [Fact]
        public void ShouldGetCommitIdFromMessageId()
        {
            var guid = Guid.NewGuid();
            var mutating = Fake<IMutating>();
            A.CallTo(() => mutating.Headers).Returns(new Dictionary<string, string>
            {
                [$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"] = guid.ToString()
            });

            Sut.MutateIncoming(mutating);

            Sut.CommitId.Should().Be(guid);
        }
        [Fact]
        public void ShouldGetCommitIdFromCommitId()
        {
            var guid = Guid.NewGuid();
            var mutating = Fake<IMutating>();
            A.CallTo(() => mutating.Headers).Returns(new Dictionary<string, string>
            {
                [$"{Defaults.PrefixHeader}.{Defaults.CommitIdHeader}"] = guid.ToString()
            });

            Sut.MutateIncoming(mutating);

            Sut.CommitId.Should().Be(guid);
        }
        [Fact]
        public void ShouldGetCorrelationId()
        {
            var guid = Guid.NewGuid();
            var mutating = Fake<IMutating>();
            A.CallTo(() => mutating.Headers).Returns(new Dictionary<string, string>
            {
                [global::NServiceBus.Headers.CorrelationId] = guid.ToString()
            });

            Sut.MutateIncoming(mutating);

            Sut.CurrentHeaders.Should().ContainKey($"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}");
            Sut.CurrentHeaders[$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"].Should().Be(guid.ToString());
        }
        [Fact]
        public void ShouldGetCommitIdFromNServiceBusMessageId()
        {
            var guid = Guid.NewGuid();
            var mutating = Fake<IMutating>();
            A.CallTo(() => mutating.Headers).Returns(new Dictionary<string, string>
            {
                [global::NServiceBus.Headers.MessageId] = guid.ToString()
            });

            Sut.MutateIncoming(mutating);

            Sut.CommitId.Should().Be(guid);
        }
        [Fact]
        public void ShouldAddWorkingHeadersToOutgoing()
        {
            var mutating = Fake<IMutating>();
            A.CallTo(() => mutating.Headers).Returns(new Dictionary<string, string>
            {
                ["test"] = "test"
            });
            Sut.MutateIncoming(mutating);

            var outgoing = Fake<IMutating>();

            Sut.MutateOutgoing(outgoing);

            outgoing.Headers.Should().ContainKey("test");
        }
    }
}
