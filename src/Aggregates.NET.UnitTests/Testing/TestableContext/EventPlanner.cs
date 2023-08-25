using Aggregates.Exceptions;
using Aggregates.Testing.TestableContext.Fakes;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Testing.TestableContext {
    public class EventPlanner : TestSubject<Aggregates.TestableContext> {
        [Fact]
        public async Task RaisesEventUsingTestableId() {

            Sut.UoW.Plan<Fakes.FakeEntity>(Sut.Id(0)).Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = Sut.Id(0), RaiseEvent = true }, Sut);


            Sut.UoW.Check<Fakes.FakeEntity>(Sut.Id(0))
                .Raised<Fakes.FakeEvent>(e => { });
        }
        [Fact]
        public async Task RaisesEvent() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId="test", RaiseEvent=true }, Sut);


            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent>(e => { });
        }
        [Fact]
        public async Task DoesntRaiseEvent() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            var act = () => Sut.UoW.Check<Fakes.FakeEntity>("test")
                            .Raised<Fakes.FakeEvent>(e => { });

            act.Should().Throw<DidNotRaisedException>();
        }

        [Fact]
        public async Task EntityExists() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            var act = () => handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            await act.Should().NotThrowAsync();
        }
        [Fact]
        public async Task EntityDoesntExists() {

            var handler = new FakeMessageHandler();
            var act = () => handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            await act.Should().ThrowAsync<NotFoundException>();
        }
        [Fact]
        public async Task EntityHasEvents() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .HasEvent<Fakes.FakeEvent>(e => { });

            var handler = new FakeMessageHandler();
            var version = await handler.GetEntityVersion(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            version.Should().Be(0);
        }
        [Fact]
        public async Task NewEntityShouldBeVersionNegOne() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            var version = await handler.GetEntityVersion(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            version.Should().Be(-1);
        }

        [Fact]
        public async Task EntityHasMultipleEvents() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .HasEvent<Fakes.FakeEvent>(e => { })
                .HasEvent<Fakes.FakeEvent>(e => { })
                .HasEvent<Fakes.FakeEvent>(e => { });

            var handler = new FakeMessageHandler();
            var version = await handler.GetEntityVersion(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            version.Should().Be(2);
        }
        [Fact]
        public async Task EntityHasSnapshot() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .HasSnapshot(new Fakes.FakeState { LoadedSnap = true });

            var handler = new FakeMessageHandler();
            var state = await handler.GetEntityState(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            state.LoadedSnap.Should().BeTrue();
        }
        [Fact]
        public async Task EntityHasNoSnapshot() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Exists();

            var handler = new FakeMessageHandler();
            var state = await handler.GetEntityState(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            state.LoadedSnap.Should().BeFalse();
        }

        [Fact]
        public async Task EntityWithAChild() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .Exists();

            var handler = new FakeMessageHandler();
            await handler.HandleInChild(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);


            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Check<Fakes.FakeChildEntity>("test")
                .Raised<Fakes.FakeEvent>(e => { });
        }
        [Fact]
        public async Task EntityWithAChildDoesntRaiseEvent() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .Exists();

            var handler = new FakeMessageHandler();
            await handler.HandleInChild(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            var act = () => Sut.UoW.Check<Fakes.FakeEntity>("test")
                            .Check<Fakes.FakeChildEntity>("test")
                            .Raised<Fakes.FakeEvent>(e => { });

            act.Should().Throw<DidNotRaisedException>();
        }

        [Fact]
        public async Task EntityWithAChildEntityExists() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .Exists();

            var handler = new FakeMessageHandler();
            var act = () => handler.HandleInChild(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            await act.Should().NotThrowAsync();
        }
        [Fact]
        public async Task EntityWithAChildEntityDoesntExists() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            var act = () => handler.HandleInChild(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            await act.Should().ThrowAsync<NotFoundException>();
        }
        [Fact]
        public async Task EntityWithAChildEntityHasEvents() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .HasEvent<Fakes.FakeEvent>(e => { });

            var handler = new FakeMessageHandler();
            var version = await handler.GetChildEntityVersion(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            version.Should().Be(0);
        }
        [Fact]
        public async Task EntityWithAChildNewEntityShouldBeVersionNegOne() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .Exists();

            var handler = new FakeMessageHandler();
            var version = await handler.GetChildEntityVersion(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            version.Should().Be(-1);
        }

        [Fact]
        public async Task EntityWithAChildEntityHasMultipleEvents() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .HasEvent<Fakes.FakeEvent>(e => { })
                .HasEvent<Fakes.FakeEvent>(e => { })
                .HasEvent<Fakes.FakeEvent>(e => { });

            var handler = new FakeMessageHandler();
            var version = await handler.GetChildEntityVersion(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            version.Should().Be(2);
        }
        [Fact]
        public async Task EntityWithAChildEntityHasSnapshot() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .HasSnapshot(new Fakes.FakeChildState { LoadedSnap = true });

            var handler = new FakeMessageHandler();
            var state = await handler.GetChildEntityState(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            state.LoadedSnap.Should().BeTrue();
        }
        [Fact]
        public async Task EntityWithAChildEntityHasNoSnapshot() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .Exists();

            var handler = new FakeMessageHandler();
            var state = await handler.GetChildEntityState(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            state.LoadedSnap.Should().BeFalse();
        }
    }
}
