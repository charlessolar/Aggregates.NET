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
            var reader = Fake<ISnapshotReader>();
            A.CallTo(() => reader.Retreive(A<string>.Ignored)).Returns((ISnapshot)null);
            Inject(reader);
            var store = Fake<IStoreEvents>();
            A.CallTo(() => store.GetEventsBackwards(A<string>.Ignored, A<long?>.Ignored, 1)).Returns(new[] { Fake<IFullEvent>() });
            Inject(store);

            await Sut.GetSnapshot<FakeEntity>("test", "test", new Id[] { }).ConfigureAwait(false);

            A.CallTo(() => store.GetEventsBackwards(A<string>.Ignored, StreamPosition.End, 1)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldGetSnapshotFromReader()
        {
            var reader = Fake<ISnapshotReader>();
            A.CallTo(() => reader.Retreive(A<string>.Ignored)).Returns(Fake<ISnapshot>());
            Inject(reader);
            var store = Fake<IStoreEvents>();
            Inject(store);

            await Sut.GetSnapshot<FakeEntity>("test", "test", new Id[] { }).ConfigureAwait(false);

            A.CallTo(() => reader.Retreive(A<string>.Ignored)).MustHaveHappened();
            A.CallTo(() => store.GetEventsBackwards(A<string>.Ignored, A<long?>.Ignored, A<int?>.Ignored)).MustNotHaveHappened();
        }
        [Fact]
        public async Task ShouldWriteSnapshot()
        {
            var store = Fake<IStoreEvents>();
            Inject(store);

            await Sut.WriteSnapshots<FakeEntity>(Fake<FakeState>(), new Dictionary<string, string>()).ConfigureAwait(false);

            A.CallTo(() => store.WriteEvents(A<string>.Ignored, A<FullEvent[]>.That.Matches(x => x.Length == 1), A<Dictionary<string, string>>.Ignored, A<long?>.Ignored)).MustHaveHappened();
        }
    }
}
