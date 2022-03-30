using Aggregates.Contracts;
using Aggregates.Internal;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class StoreSnapshots : TestSubject<Internal.StoreSnapshots>
    {
        [Fact]
        public async Task ShouldGetSnapshotFromEventStore()
        {
            var store = Fake<IStoreEvents>();
            A.CallTo(() => store.GetSnapshot<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Fake<ISnapshot>());

            await Sut.GetSnapshot<FakeEntity, FakeState>("test", "test", new Id[] { }).ConfigureAwait(false);

            A.CallTo(() => store.GetSnapshot<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldWriteSnapshot()
        {
            var store = Fake<IStoreEvents>();

            await Sut.WriteSnapshots<FakeEntity>(Fake<FakeState>(), new Dictionary<string, string>()).ConfigureAwait(false);

            A.CallTo(() => store.WriteSnapshot<FakeEntity>(A<ISnapshot>.Ignored, A<Dictionary<string, string>>.Ignored)).MustHaveHappened();
        }
    }
}
