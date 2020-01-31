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
    public class UnitOfWork : TestSubject<Internal.UnitOfWork>
    {
        [Fact]
        public void ShouldCreateRepository()
        {
            var factory = Fake<IRepositoryFactory>();
            Inject(factory);

            Sut.For<FakeEntity>();
            Sut.For<FakeChildEntity, FakeEntity>(Fake<FakeEntity>());

            A.CallTo(() => factory.ForEntity<FakeEntity>()).MustHaveHappened();
            A.CallTo(() => factory.ForEntity<FakeChildEntity, FakeEntity>(A<FakeEntity>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public void ShouldCreateSameRepository()
        {
            Inject<Id>("test");
            var factory = Fake<IRepositoryFactory>();
            Inject(factory);

            Sut.For<FakeEntity>();
            Sut.For<FakeEntity>();
            Sut.For<FakeChildEntity, FakeEntity>(Fake<FakeEntity>());
            Sut.For<FakeChildEntity, FakeEntity>(Fake<FakeEntity>());

            A.CallTo(() => factory.ForEntity<FakeEntity>()).MustHaveHappenedOnceExactly();
            A.CallTo(() => factory.ForEntity<FakeChildEntity, FakeEntity>(A<FakeEntity>.Ignored)).MustHaveHappenedOnceExactly();
        }
        [Fact]
        public async Task ShouldCommitOnEnd()
        {
            var repo = new FakeRepository();
            Inject<IRepository<FakeEntity>>(repo);

            Sut.For<FakeEntity>();
            Sut.CommitId = Guid.NewGuid();
            await (Sut as Aggregates.UnitOfWork.IUnitOfWork).End().ConfigureAwait(false);

            repo.CommitCalled.Should().BeTrue();
        }
        [Fact]
        public async Task ShouldNotCommitIfNoCommitId()
        {
            var repo = new FakeRepository();
            Inject<IRepository<FakeEntity>>(repo);

            Sut.For<FakeEntity>();
            Sut.CommitId = Guid.Empty;

            var e = await Record.ExceptionAsync(() => (Sut as Aggregates.UnitOfWork.IUnitOfWork).End()).ConfigureAwait(false);
            e.Should().BeOfType<InvalidOperationException>();
        }
        [Fact]
        public async Task ShouldNotCommitOnEndWithException()
        {
            var repo = new FakeRepository();
            Inject<IRepository<FakeEntity>>(repo);

            Sut.For<FakeEntity>();
            await (Sut as Aggregates.UnitOfWork.IUnitOfWork).End(new Exception()).ConfigureAwait(false);

            repo.CommitCalled.Should().BeFalse();
        }
        [Fact]
        public async Task ShouldPrepareAllRepositoriesWhenMultipleChangedStreams()
        {
            var repo = new FakeRepository();
            repo.FakeChangedStreams = 2;
            Inject<IRepository<FakeEntity>>(repo);

            Sut.For<FakeEntity>();
            Sut.CommitId = Guid.NewGuid();
            await (Sut as Aggregates.UnitOfWork.IUnitOfWork).End().ConfigureAwait(false);

            repo.PrepareCalled.Should().BeTrue();
        }
        [Fact]
        public void ShouldDisposeAllRepositories()
        {
            var repo = Fake<IRepository<FakeEntity>>();
            Inject(repo);

            Sut.For<FakeEntity>();
            (Sut as IDisposable).Dispose();

            A.CallTo(() => (repo as IDisposable).Dispose()).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldNotCommitWhenProcessingAnEvent()
        {
            var repo = new FakeRepository();
            Inject<IRepository<FakeEntity>>(repo);

            Sut.For<FakeEntity>();
            Sut.CurrentMessage = Fake<Messages.IEvent>();
            await (Sut as Aggregates.UnitOfWork.IUnitOfWork).End().ConfigureAwait(false);

            repo.CommitCalled.Should().BeFalse();
        }
    }
}
