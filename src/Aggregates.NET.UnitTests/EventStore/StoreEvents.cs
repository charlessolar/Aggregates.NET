using Aggregates.Contracts;
using Aggregates.Exceptions;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.EventStore
{
    public class StoreEvents : TestSubject<Internal.StoreEvents>
    {
        [Fact]
        public async Task SnapshotReadsLast()
        {
            var client = Fake<IEventStoreClient>();
            await Sut.GetSnapshot<FakeEntity>("test", "test", null);
            A.CallTo(() => client.GetEvents(Internal.StreamDirection.Backwards, A<string>.Ignored, A<long?>.Ignored, 1)).MustHaveHappened();
        }
        [Fact]
        public async Task SnapshotStreamDoesntExist()
        {
            var client = Fake<IEventStoreClient>();
            A.CallTo(() => client.GetEvents(A<Internal.StreamDirection>.Ignored, A<string>.Ignored, A<long?>.Ignored, A<int?>.Ignored)).Throws<NotFoundException>();

            var result = await Sut.GetSnapshot<FakeEntity>("test", "test", null);
            result.Should().BeNull();
        }
    }
}
