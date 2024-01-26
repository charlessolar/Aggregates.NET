using Aggregates.Contracts;
using Aggregates.Extensions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common.Extensions
{
    public class Reflection : Test
    {
        class FakeState : Aggregates.State<FakeState>
        {
            private void Handle(int one) { }
            public void Handle(string one) { }
        }
        class FakeService :
            IProvideService<IService<int>, int>
        {
            public Task<int> Handle(IService<int> one, IServiceContext context) { return Task.FromResult(1); }
        }

        [Fact]
        public void ShouldGetHandleInfoFromState()
        {
            var mutator = ReflectionExtensions.GetStateMutators<FakeState>();
            mutator.Should().ContainKey("Handle.Int32");
        }
        [Fact]
        public void ShouldNotGetPublicHandleInfoFromState()
        {
            var mutator = ReflectionExtensions.GetStateMutators<FakeState>();
            mutator.Should().NotContainKey("Handle.String");
        }
        [Fact]
        public void ShouldNotGetPublicConflictInfoFromState()
        {
            var mutator = ReflectionExtensions.GetStateMutators<FakeState>();
            mutator.Should().NotContainKey("Conflict.String");
        }
        [Fact]
        public async Task ShouldGetServiceHandler()
        {
            var handler = ReflectionExtensions.MakeServiceHandler<IService<int>, int>(typeof(FakeService));

            var result = await handler(new FakeService(), Fake<IService<int>>(), Fake<IServiceContext>());

            result.Should().Be(1);
        }
        [Fact]
        public void ShouldCreateRepositoryFactory()
        {
            var factory = ReflectionExtensions.BuildRepositoryFunc<FakeEntity>();
            factory.Should().NotBeNull();
            factory(Fake<ILogger>(), Fake<IStoreEntities>()).Should().BeAssignableTo<IRepository<FakeEntity>>();
        }
        [Fact]
        public void ShouldCreateChildRepositoryFactory()
        {
            var factory = ReflectionExtensions.BuildParentRepositoryFunc<FakeChildEntity, FakeEntity>();
            factory.Should().NotBeNull();
            factory(Fake<ILogger>(), Fake<FakeEntity>(), Fake<IStoreEntities>()).Should().BeAssignableTo<IRepository<FakeChildEntity, FakeEntity>>();
        }
    }
}
