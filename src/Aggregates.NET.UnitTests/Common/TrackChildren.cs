using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class TrackChildren : TestSubject<Internal.TrackChildren>
    {
        [Fact]
        public async Task NotEnabledThrows()
        {
            var settings = Fake<ISettings>();
            A.CallTo(() => settings.TrackChildren).Returns(false);

            await Sut.Setup("test", new Version("1.0.0"));
            Func<Task> act = () => Sut.GetChildren<FakeChildEntity, FakeEntity>(Fake<Aggregates.UnitOfWork.IDomainUnitOfWork>(), Fake<FakeEntity>());
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        [Fact]
        public async Task NotSetupThrows()
        {
            var settings = Fake<ISettings>();
            A.CallTo(() => settings.TrackChildren).Returns(true);

            //await Sut.Setup("test", new Version("1.0.0"));

            Func<Task> act = () => Sut.GetChildren<FakeChildEntity, FakeEntity>(Fake<Aggregates.UnitOfWork.IDomainUnitOfWork>(), Fake<FakeEntity>());
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        [Fact]
        public async Task SetupCallsProjectionCreation()
        {
            var settings = Fake<ISettings>();
            var consumer = Fake<IEventStoreConsumer>();
            A.CallTo(() => settings.TrackChildren).Returns(true);

            await Sut.Setup("test", new Version("1.0.0"));

            A.CallTo(() => consumer.SetupChildrenProjection(A<string>.Ignored, A<Version>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task CallingSetupNotEnabledDoesNotCreateProjection()
        {
            var settings = Fake<ISettings>();
            var consumer = Fake<IEventStoreConsumer>();
            A.CallTo(() => settings.TrackChildren).Returns(false);

            await Sut.Setup("test", new Version("1.0.0"));

            A.CallTo(() => consumer.SetupChildrenProjection(A<string>.Ignored, A<Version>.Ignored)).MustNotHaveHappened();
        }
        [Fact]
        public async Task GetChildrenReturnsNull()
        {
            var settings = Fake<ISettings>();
            var consumer = Fake<IEventStoreConsumer>();
            A.CallTo(() => settings.TrackChildren).Returns(true);

            await Sut.Setup("test", new Version("1.0.0"));

            A.CallTo(() => consumer.GetChildrenData<FakeEntity>(A<Version>.Ignored, A<FakeEntity>.Ignored)).Returns((Internal.ChildrenProjection)null);

            var children = await Sut.GetChildren<FakeChildEntity, FakeEntity>(Fake<Aggregates.UnitOfWork.IDomainUnitOfWork>(), Fake<FakeEntity>());
            children.Should().BeEmpty();
        }
        [Fact]
        public async Task GetChildrenHasChildrenNull()
        {
            var settings = Fake<ISettings>();
            var consumer = Fake<IEventStoreConsumer>();
            A.CallTo(() => settings.TrackChildren).Returns(true);

            await Sut.Setup("test", new Version("1.0.0"));

            var childrenProjection = new Internal.ChildrenProjection
            {
                Children = null
            };

            A.CallTo(() => consumer.GetChildrenData<FakeEntity>(A<Version>.Ignored, A<FakeEntity>.Ignored)).Returns(childrenProjection);

            var children = await Sut.GetChildren<FakeChildEntity, FakeEntity>(Fake<Aggregates.UnitOfWork.IDomainUnitOfWork>(), Fake<FakeEntity>());
            children.Should().BeEmpty();
        }
        [Fact]
        public async Task GetChildrenHasChildrenDifferentType()
        {
            var settings = Fake<ISettings>();
            var consumer = Fake<IEventStoreConsumer>();
            A.CallTo(() => settings.TrackChildren).Returns(true);

            await Sut.Setup("test", new Version("1.0.0"));

            var childrenProjection = new Internal.ChildrenProjection
            {
                Children = new[] {
                    new Internal.ChildrenProjection.ChildDescriptor
                    {
                        EntityType="test",
                        StreamId="test"
                    }
                }
            };

            A.CallTo(() => consumer.GetChildrenData<FakeEntity>(A<Version>.Ignored, A<FakeEntity>.Ignored)).Returns(childrenProjection);

            var children = await Sut.GetChildren<FakeChildEntity, FakeEntity>(Fake<Aggregates.UnitOfWork.IDomainUnitOfWork>(), Fake<FakeEntity>());
            children.Should().BeEmpty();
        }
        [Fact]
        public async Task GetChildrenWithChildren()
        {
            var settings = Fake<ISettings>();
            var consumer = Fake<IEventStoreConsumer>();
            A.CallTo(() => settings.TrackChildren).Returns(true);

            var registrar = Fake<IVersionRegistrar>();
            A.CallTo(() => registrar.GetVersionedName(A<Type>.Ignored, A<bool>.Ignored)).Returns("test");
            A.CallTo(() => registrar.GetNamedType(A<string>.Ignored)).Returns(typeof(FakeChildEntity));

            await Sut.Setup("test", new Version("1.0.0"));

            var childrenProjection = new Internal.ChildrenProjection
            {
                Children = new[] {
                    new Internal.ChildrenProjection.ChildDescriptor
                    {
                        EntityType="test",
                        StreamId="test"
                    }
                }
            };

            A.CallTo(() => consumer.GetChildrenData<FakeEntity>(A<Version>.Ignored, A<FakeEntity>.Ignored)).Returns(childrenProjection);

            var children = await Sut.GetChildren<FakeChildEntity, FakeEntity>(Fake<Aggregates.UnitOfWork.IDomainUnitOfWork>(), Fake<FakeEntity>());
            children.Should().NotBeEmpty();

        }
    }
}
