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

namespace Aggregates.Common
{
    public class Repository : TestSubject<Internal.Repository<FakeEntity, FakeState>>
    {
        [Fact]
        public async Task ShouldGetEntityFromTryGet()
        {
            var task = Sut.TryGet("test");
            var entity = await task.ConfigureAwait(false);
            entity.Should().NotBeNull();
        }
        [Fact]
        public async Task ShouldGetEntityFromTryGetWithNullId()
        {
            var entity = await Sut.TryGet((Id)null).ConfigureAwait(false);
            entity.Should().BeNull();
        }
        [Fact]
        public async Task ShouldNotGetEntityFromTryGet()
        {
            var store = Fake<IStoreEntities>();
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<IEntity>.Ignored)).Throws<NotFoundException>();
            Inject(store);

            var entity = await Sut.TryGet("test").ConfigureAwait(false);
            entity.Should().BeNull();
        }
        [Fact]
        public async Task ShouldGetEntityFromGet()
        {
            var entity = await Sut.Get("test").ConfigureAwait(false);
            entity.Should().NotBeNull();
        }
        [Fact]
        public async Task ShouldGetExceptionFromGetUnknown()
        {
            var store = Fake<IStoreEntities>();
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<IEntity>.Ignored)).Throws<NotFoundException>();
            Inject(store);

            var e = await Record.ExceptionAsync(() => Sut.Get("test")).ConfigureAwait(false);
            e.Should().BeOfType<NotFoundException>();
        }
        [Fact]
        public async Task ShouldGetExistingEntityAgain()
        {
            var entity = await Sut.Get("test").ConfigureAwait(false);
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            var entity2 = await Sut.Get("test").ConfigureAwait(false);
            entity2.State.Version.Should().Be(5L);
        }
        [Fact]
        public async Task ShouldGetNewEntity()
        {
            var entity = await Sut.New("test").ConfigureAwait(false);
            entity.Should().NotBeNull();
        }
        [Fact]
        public async Task ShouldGetExistingEntityOnNew()
        {
            var entity = await Sut.New("test").ConfigureAwait(false);
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            var entity2 = await Sut.New("test").ConfigureAwait(false);
            entity2.State.Version.Should().Be(5L);
        }
        [Fact]
        public async Task ShouldHaveNoChangedStreams()
        {
            Sut.ChangedStreams.Should().Be(0);

            var entity = await Sut.Get("test").ConfigureAwait(false);
            Sut.ChangedStreams.Should().Be(0);
        }
        [Fact]
        public async Task ShouldHaveChangedStreams()
        {
            var entity = await Sut.Get("test").ConfigureAwait(false);
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            Sut.ChangedStreams.Should().Be(0);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());
            Sut.ChangedStreams.Should().Be(1);
        }
        [Fact]
        public async Task PrepareShouldVerifyVersionOnUnchangedEntities()
        {
            var store = Fake<IStoreEntities>();
            Inject(store);
            await Sut.Get("test").ConfigureAwait(false);

            await (Sut as IRepositoryCommit).Prepare(Guid.NewGuid()).ConfigureAwait(false);

            A.CallTo(() => store.Verify<FakeEntity, FakeState>(A<FakeEntity>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task PrepareShoudNotVerifyVersionOnChangedEntities()
        {
            var store = Fake<IStoreEntities>();
            Inject(store);
            var entity = await Sut.Get("test").ConfigureAwait(false);
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            await (Sut as IRepositoryCommit).Prepare(Guid.NewGuid()).ConfigureAwait(false);

            A.CallTo(() => store.Verify<FakeEntity, FakeState>(A<FakeEntity>.Ignored)).MustNotHaveHappened();
        }
        [Fact]
        public async Task CommitShouldSaveChangedEntities()
        {
            var store = Fake<IStoreEntities>();
            Inject(store);
            var entity = await Sut.Get("test").ConfigureAwait(false);
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            await (Sut as IRepositoryCommit).Commit(Guid.NewGuid(), new Dictionary<string,string>()).ConfigureAwait(false);

            A.CallTo(() => store.Commit<FakeEntity, FakeState>(A<FakeEntity>.Ignored, A<Guid>.Ignored, A<Dictionary<string, string>>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task CommitShouldNotSaveUnchangedEntities()
        {
            var store = Fake<IStoreEntities>();
            Inject(store);
            var entity = await Sut.Get("test").ConfigureAwait(false);

            await (Sut as IRepositoryCommit).Commit(Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);

            A.CallTo(() => store.Commit<FakeEntity, FakeState>(A<FakeEntity>.Ignored, A<Guid>.Ignored, A<Dictionary<string, string>>.Ignored)).MustNotHaveHappened();
        }
    }
}
