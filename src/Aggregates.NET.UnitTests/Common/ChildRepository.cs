using Aggregates.Contracts;
using Aggregates.Exceptions;
using FakeItEasy;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class ChildRepository : TestSubject<Internal.Repository<FakeChildEntity, FakeChildState, FakeEntity, FakeState>>
    {
        [Fact]
        public async Task ShouldGetEntityFromTryGet()
        {
            var entity = await Sut.TryGet("test");
            entity.Should().NotBeNull();
        }
        [Fact]
        public async Task ShouldNotGetEntityFromTryGet()
        {
            var store = Fake<IStoreEntities>();
            A.CallTo(() => store.Get<FakeChildEntity, FakeChildState>(A<string>.Ignored, A<Id>.Ignored, A<IEntity>.Ignored)).Throws<NotFoundException>();

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
            A.CallTo(() => store.Get<FakeChildEntity, FakeChildState>(A<string>.Ignored, A<Id>.Ignored, A<IEntity>.Ignored)).Throws<NotFoundException>();

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
    }
}
