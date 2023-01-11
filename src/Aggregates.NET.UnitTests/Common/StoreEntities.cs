using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class StoreEntities : TestSubject<Internal.StoreEntities>
    {
        readonly IStoreSnapshots Snapstore;

        public StoreEntities()
        {
            var fakeuow = new Fake<Aggregates.UnitOfWork.IDomainUnitOfWork>();
            A.CallTo(() => Provider.GetService(typeof(Aggregates.UnitOfWork.IDomainUnitOfWork))).Returns(fakeuow.FakedObject);

            Snapstore = Fake<IStoreSnapshots>();
            A.CallTo(() => Snapstore.GetSnapshot<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult((ISnapshot)null));
        }

        [Fact]
        public async Task ShouldCreateNewEntity()
        {
            var entity = await Sut.New<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);
            entity.Version.Should().Be(Internal.EntityFactory.NewEntityVersion);
        }
        [Fact]
        public async Task ShouldCreateChildEntity()
        {
            var parent = Fake<IEntity>();
            A.CallTo(() => parent.Id).Returns("parent");
            var entity = await Sut.New<FakeChildEntity, FakeChildState>("test", "test", parent).ConfigureAwait(false);
            entity.Version.Should().Be(Internal.EntityFactory.NewEntityVersion);
            entity.State.Parents.Any(x => x.StreamId == "parent").Should().BeTrue();
        }
        [Fact]
        public async Task ShouldGetEntityNoSnapshot()
        {
            var snapstore = Fake<IStoreSnapshots>();
            A.CallTo(() => snapstore.GetSnapshot<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult((ISnapshot)null));

            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);

            entity.State.Snapshot.Should().BeNull();
        }
        [Fact]
        public async Task ShouldGetEntityWithSnapshot()
        {
            var snapshot = Fake<ISnapshot>();
            A.CallTo(() => snapshot.Payload).Returns(new FakeState() { ThrowAbandon = true });
            A.CallTo(() => Snapstore.GetSnapshot<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult(snapshot));

            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);

            entity.State.ThrowAbandon.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldGetEventStreamFromSnapshotVersionOn()
        {
            var snapshot = Fake<ISnapshot>();
            A.CallTo(() => snapshot.Version).Returns(1);
            A.CallTo(() => snapshot.Payload).Returns(new FakeState() { Version = 1 });
            A.CallTo(() => Snapstore.GetSnapshot<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult(snapshot));
            var eventstore = Fake<IStoreEvents>();

            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);

            // Verify GetEvents from version 1 was called
            A.CallTo(() => eventstore.GetEvents<FakeEntity>(A<StreamDirection>.Ignored, A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, 1, A<int?>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldGetChildEntity()
        {
            var parent = Fake<IEntity>();
            A.CallTo(() => parent.Id).Returns("parent");
            A.CallTo(() => Snapstore.GetSnapshot<FakeChildEntity, FakeChildState>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored)).Returns(Task.FromResult((ISnapshot)null));

            var entity = await Sut.Get<FakeChildEntity, FakeChildState>("test", "test", parent).ConfigureAwait(false);

            entity.State.Parents.Any(x => x.StreamId == "parent").Should().BeTrue();
        }
        [Fact]
        public async Task ShouldVerifyVersion()
        {
            var eventstore = Fake<IStoreEvents>();
            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);

            await Sut.Verify<FakeEntity, FakeState>(entity).ConfigureAwait(false);

            A.CallTo(() => eventstore.VerifyVersion<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<long>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldNotVerifyNewEntity()
        {
            var entity = await Sut.New<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);

            var e = await Record.ExceptionAsync(() => Sut.Verify<FakeEntity, FakeState>(entity)).ConfigureAwait(false);
            e.Should().BeOfType<ArgumentException>();
        }
        [Fact]
        public async Task ShouldNotVerifyDirtyEntity()
        {
            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            var e = await Record.ExceptionAsync(() => Sut.Verify<FakeEntity, FakeState>(entity)).ConfigureAwait(false);

            e.Should().BeOfType<ArgumentException>();
        }
        [Fact]
        public async Task ShouldNotCommitCleanEntity()
        {
            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);

            var e = await Record.ExceptionAsync(() => Sut.Commit<FakeEntity, FakeState>(entity, Guid.NewGuid(), new Dictionary<string, string>())).ConfigureAwait(false);
            e.Should().BeOfType<ArgumentException>();
        }
        [Fact]
        public async Task ShouldCommitDomainEvents()
        {
            var store = Fake<IStoreEvents>();
            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            await Sut.Commit<FakeEntity, FakeState>(entity, Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);

            A.CallTo(() => store.WriteEvents<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.That.Matches(x => x.Length == 3), A<Dictionary<string, string>>.Ignored, A<long?>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldCommitSnapshot()
        {
            var snapstore = Fake<IStoreSnapshots>();
            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());
            entity.State.TakeASnapshot = true;

            await Sut.Commit<FakeEntity, FakeState>(entity, Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);

            A.CallTo(() => snapstore.WriteSnapshots<FakeEntity>(A<IState>.Ignored, A<Dictionary<string, string>>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldNotThrowOnSnapshotFailure()
        {
            var snapstore = Fake<IStoreSnapshots>();
            A.CallTo(() => snapstore.WriteSnapshots<FakeEntity>(A<IState>.Ignored, A<Dictionary<string, string>>.Ignored)).Throws<Exception>();
            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());
            entity.State.TakeASnapshot = true;

            var e = await Record.ExceptionAsync(() => Sut.Commit<FakeEntity, FakeState>(entity, Guid.NewGuid(), new Dictionary<string, string>())).ConfigureAwait(false);
            e.Should().BeNull();
        }

        [Fact]
        public async Task ShouldThrowPersistenceException()
        {
            var store = Fake<IStoreEvents>();
            A.CallTo(() => store.WriteEvents<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.Ignored, A<Dictionary<string, string>>.Ignored, A<long?>.Ignored)).Throws(new PersistenceException("test", new Exception()));
            var entity = await Sut.Get<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            var e = await Record.ExceptionAsync(() => Sut.Commit<FakeEntity, FakeState>(entity, Guid.NewGuid(), new Dictionary<string, string>())).ConfigureAwait(false);

            e.Should().BeOfType<PersistenceException>();
        }
        [Fact]
        public async Task ShouldThrowEntityAlreadyExistsException()
        {
            var store = Fake<IStoreEvents>();
            A.CallTo(() => store.WriteEvents<FakeEntity>(A<string>.Ignored, A<Id>.Ignored, A<Id[]>.Ignored, A<IFullEvent[]>.Ignored, A<Dictionary<string, string>>.Ignored, A<long?>.Ignored)).Throws(new VersionException("test", new Exception()));
            var entity = await Sut.New<FakeEntity, FakeState>("test", "test", null).ConfigureAwait(false);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            var e = await Record.ExceptionAsync(() => Sut.Commit<FakeEntity, FakeState>(entity, Guid.NewGuid(), new Dictionary<string, string>())).ConfigureAwait(false);

            e.Should().BeOfType<EntityAlreadyExistsException>();
        }
    }
}
