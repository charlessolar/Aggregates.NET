using Aggregates.Contracts;
using FakeItEasy;
using System.Collections.Generic;
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

            await Sut.GetSnapshot<FakeEntity, FakeState>("test", "test", new Id[] { });

            A.CallTo(() => store.GetSnapshot<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldWriteSnapshot()
        {
            var store = Fake<IStoreEvents>();

            await Sut.WriteSnapshots<FakeEntity>(Fake<FakeState>(), new Dictionary<string, string>());

            A.CallTo(() => store.WriteSnapshot<FakeEntity>(A<ISnapshot>.Ignored, A<Dictionary<string, string>>.Ignored)).MustHaveHappened();
        }
    }
}
