using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class StoreEntities : TestSubject<Internal.StoreEntities>
    {
        [Fact]
        public async Task ShouldCreateNewEntity()
        {
            var entity = await Sut.New<FakeEntity, FakeState>("test", "test", new Id[] { }).ConfigureAwait(false);
            entity.Version.Should().Be(Internal.EntityFactory.NewEntityVersion);
        }
        [Fact]
        public async Task ShouldCreateChildEntity()
        {
            var entity = await Sut.New<FakeEntity, FakeState>("test", "test", new Id[] { "parent" }).ConfigureAwait(false);
            entity.Version.Should().Be(Internal.EntityFactory.NewEntityVersion);
            entity.Parents.Should().Contain("parent");
        }
        [Fact]
        public async Task ShouldGetEntityNoSnapshot()
        {
            var snapstore = Fake<IStoreSnapshots>();
            A.CallTo(() => snapstore.GetSnapshot<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult((ISnapshot)null));
            Inject(snapstore);

            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", new Id[] { }).ConfigureAwait(false);

            entity.State.Snapshot.Should().BeNull();
        }
        [Fact]
        public async Task ShouldGetEntityWithSnapshot()
        {
            var snapshot = Fake<ISnapshot>();
            A.CallTo(() => snapshot.Payload).Returns(new FakeState() { ThrowAbandon = true });
            var snapstore = Fake<IStoreSnapshots>();
            A.CallTo(() => snapstore.GetSnapshot<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult(snapshot));
            Inject(snapstore);

            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", new Id[] { }).ConfigureAwait(false);

            entity.State.ThrowAbandon.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldGetEventStreamFromSnapshotVersionOn()
        {
            var snapshot = Fake<ISnapshot>();
            A.CallTo(() => snapshot.Version).Returns(1);
            A.CallTo(() => snapshot.Payload).Returns(new FakeState() { Version = 1 });
            var snapstore = Fake<IStoreSnapshots>();
            A.CallTo(() => snapstore.GetSnapshot<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult(snapshot));
            Inject(snapstore);
            var eventstore = Fake<IStoreEvents>();
            Inject(eventstore);

            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", new Id[] { }).ConfigureAwait(false);

            // Verify GetEvents from version 1 was called
            A.CallTo(() => eventstore.GetEvents<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, 1, A<int?>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldGetChildEntity()
        {
            var snapstore = Fake<IStoreSnapshots>();
            A.CallTo(() => snapstore.GetSnapshot<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult((ISnapshot)null));
            Inject(snapstore);

            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", new Id[] { "parent" }).ConfigureAwait(false);

            entity.Parents.Should().Contain("parent");
        }
        [Fact]
        public async Task ShouldVerifyVersion()
        {
            var snapstore = Fake<IStoreSnapshots>();
            A.CallTo(() => snapstore.GetSnapshot<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult((ISnapshot)null));
            Inject(snapstore);
            var eventstore = Fake<IStoreEvents>();
            Inject(eventstore);
            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", new Id[] { }).ConfigureAwait(false);

            await Sut.Verify<FakeEntity, FakeState>(entity).ConfigureAwait(false);

            A.CallTo(() => eventstore.VerifyVersion<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<long>.Ignored)).MustHaveHappened();
        }
    }
}
