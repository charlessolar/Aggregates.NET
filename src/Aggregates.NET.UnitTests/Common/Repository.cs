using Aggregates.Contracts;
using Aggregates.Exceptions;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
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
            var entity = await task;
            entity.Should().NotBeNull();
        }
        [Fact]
        public async Task ShouldGetEntityFromTryGetWithNullId()
        {
            var entity = await Sut.TryGet((Id)null);
            entity.Should().BeNull();
        }
        [Fact]
        public async Task ShouldNotGetEntityFromTryGet()
        {
            var store = Fake<IStoreEntities>();
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<IEntity>.Ignored)).Throws<NotFoundException>();

            var entity = await Sut.TryGet("test");
            entity.Should().BeNull();
        }
        [Fact]
        public async Task ShouldGetEntityFromGet()
        {
            var entity = await Sut.Get("test");
            entity.Should().NotBeNull();
        }
        [Fact]
        public async Task ShouldGetExceptionFromGetUnknown()
        {
            var store = Fake<IStoreEntities>();
            A.CallTo(() => store.Get<FakeEntity, FakeState>(A<string>.Ignored, A<Id>.Ignored, A<IEntity>.Ignored)).Throws<NotFoundException>();

            var e = await Record.ExceptionAsync(() => Sut.Get("test"));
            e.Should().BeOfType<NotFoundException>();
        }
        [Fact]
        public async Task ShouldGetExistingEntityAgain()
        {
            var entity = await Sut.Get("test");
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            var entity2 = await Sut.Get("test");
            entity2.State.Version.Should().Be(5L);
        }
        [Fact]
        public async Task ShouldGetNewEntity()
        {
            var entity = await Sut.New("test");
            entity.Should().NotBeNull();
        }
        [Fact]
        public async Task ShouldGetExistingEntityOnNew()
        {
            var entity = await Sut.New("test");
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            var entity2 = await Sut.New("test");
            entity2.State.Version.Should().Be(5L);
        }
        [Fact]
        public async Task ShouldHaveNoChangedStreams()
        {
            Sut.ChangedStreams.Should().Be(0);

            var entity = await Sut.Get("test");
            Sut.ChangedStreams.Should().Be(0);
        }
        [Fact]
        public async Task ShouldHaveChangedStreams()
        {
            var entity = await Sut.Get("test");
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            Sut.ChangedStreams.Should().Be(0);
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());
            Sut.ChangedStreams.Should().Be(1);
        }
        [Fact]
        public async Task PrepareShouldVerifyVersionOnUnchangedEntities()
        {
            var store = Fake<IStoreEntities>();
            await Sut.Get("test");

            await (Sut as IRepositoryCommit).Prepare(Guid.NewGuid());

            A.CallTo(() => store.Verify<FakeEntity, FakeState>(A<FakeEntity>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task PrepareShoudNotVerifyVersionOnChangedEntities()
        {
            var store = Fake<IStoreEntities>();
            var entity = await Sut.Get("test");
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            await (Sut as IRepositoryCommit).Prepare(Guid.NewGuid());

            A.CallTo(() => store.Verify<FakeEntity, FakeState>(A<FakeEntity>.Ignored)).MustNotHaveHappened();
        }
        [Fact]
        public async Task CommitShouldSaveChangedEntities()
        {
            var store = Fake<IStoreEntities>();
            var entity = await Sut.Get("test");
            (entity as INeedVersionRegistrar).Registrar = Fake<IVersionRegistrar>();
            entity.ApplyEvents(Many<FakeDomainEvent.FakeEvent>());

            await (Sut as IRepositoryCommit).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            A.CallTo(() => store.Commit<FakeEntity, FakeState>(A<FakeEntity>.Ignored, A<Guid>.Ignored, A<Dictionary<string, string>>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task CommitShouldNotSaveUnchangedEntities()
        {
            var store = Fake<IStoreEntities>();
            var entity = await Sut.Get("test");

            await (Sut as IRepositoryCommit).Commit(Guid.NewGuid(), new Dictionary<string, string>());

            A.CallTo(() => store.Commit<FakeEntity, FakeState>(A<FakeEntity>.Ignored, A<Guid>.Ignored, A<Dictionary<string, string>>.Ignored)).MustNotHaveHappened();
        }
    }
}
