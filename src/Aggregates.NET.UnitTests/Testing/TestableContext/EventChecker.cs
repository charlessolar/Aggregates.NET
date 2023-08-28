using Aggregates.Exceptions;
using Aggregates.Testing.TestableContext.Fakes;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Testing.TestableContext {
    public class EventChecker : TestSubject<Aggregates.TestableContext> {

        [Fact]
        public async Task RaisesSpecificEvent() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true, Content="test" }, Sut);


            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent>(e => { e.Content = "test"; });
        }
        [Fact]
        public async Task RaisesSpecificEventTestableId() {

            Sut.UoW.Plan<Fakes.FakeEntity>(Sut.Id()).Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = Sut.Id(), RaiseEvent = true, Content = "test" }, Sut);


            Sut.UoW.Check<Fakes.FakeEntity>(Sut.Id())
                .Raised<Fakes.FakeEvent>(e => { e.Content = "test"; });
        }
        [Fact]
        public async Task RaisesSpecificEventInChildTestableId() {

            Sut.UoW.Plan<Fakes.FakeEntity>(Sut.Id())
                .Plan<Fakes.FakeChildEntity>(Sut.Id())
                .Exists();

            var handler = new FakeMessageHandler();
            await handler.HandleInChild(new FakeCommand { EntityId = Sut.Id(), RaiseEvent = true }, Sut);


            Sut.UoW.Check<Fakes.FakeEntity>(Sut.Id())
                .Check<Fakes.FakeChildEntity>(Sut.Id())
                .Raised<Fakes.FakeEvent>(e => { });
        }
        [Fact]
        public async Task RaisesDifferentTypeButSameData() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.HandleOther(new FakeCommand { EntityId = "test", RaiseEvent = true, Content = "test" }, Sut);


            var act = () => Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent>(e => { e.Content = "test"; });

            act.Should().Throw<DidNotRaisedException>();
        }

        [Fact]
        public async Task ChildRaisedEventNotRaisedInParent() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .Exists();

            var handler = new FakeMessageHandler();
            await handler.HandleInChild(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);


            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Check<Fakes.FakeChildEntity>("test")
                .Raised<Fakes.FakeEvent>(e => { });

            var act = () => Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent>(e => {  });

            act.Should().Throw<DidNotRaisedException>();
        }

        [Fact]
        public async Task HasEventsDoesntCountAsRaised() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .HasEvent<Fakes.FakeEvent>(x => { });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .NotRaised<Fakes.FakeEvent>();
        }

        [Fact]
        public async Task MultipleRaisedEventsDifferentContent() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true, Content = "test1" }, Sut);
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true, Content = "test2" }, Sut);
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true, Content = "test3" }, Sut);


            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent>(e => { e.Content = "test1"; })
                .Raised<Fakes.FakeEvent>(e => { e.Content = "test2"; })
                .Raised<Fakes.FakeEvent>(e => { e.Content = "test3"; });
        }

        [Fact]
        public async Task RaisedEventType() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent>();
        }
        [Fact]
        public async Task DidntRaisedEventType() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            var act = () => Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent2>();

            act.Should().Throw<NoMatchingEventException>();
        }

        [Fact]
        public async Task RaisedEventWithAssert() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true, Content ="test" }, Sut);

            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent>(x => {
                    return x.Content == "test";
                });
        }
        [Fact]
        public async Task RaisedEventWithAssertFails() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true, Content = "test" }, Sut);

            var act = () => Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Raised<Fakes.FakeEvent>(x => {
                    return x.Content == "test2";
                });

            act.Should().Throw<NoMatchingEventException>();
        }

        [Fact]
        public async Task EntityRemainsUnchanged() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            Sut.UoW.Check<Fakes.FakeEntity>("test").Unchanged();

        }
        [Fact]
        public async Task EntityWithEventsRemainsUnchanged() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .HasEvent<Fakes.FakeEvent>(x => { });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = false }, Sut);

            Sut.UoW.Check<Fakes.FakeEntity>("test").Unchanged();

        }


        [Fact]
        public async Task ParentRemainsUnchangedWhenChildIs() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test")
                .Plan<Fakes.FakeChildEntity>("test")
                .Exists();

            var handler = new FakeMessageHandler();
            await handler.HandleInChild(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            Sut.UoW.Check<Fakes.FakeEntity>("test").Unchanged();

            Sut.UoW.Check<Fakes.FakeEntity>("test")
                .Check<Fakes.FakeChildEntity>("test")
                .Raised<Fakes.FakeEvent>(e => { });

        }
        [Fact]
        public async Task EntityIsChanged() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            var act = () => Sut.UoW.Check<Fakes.FakeEntity>("test").Unchanged();
            act.Should().Throw<RaisedException>();
        }

        [Fact]
        public async Task EntityRaisedEvent() {

            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test", RaiseEvent = true }, Sut);

            var act = () => Sut.UoW.Check<Fakes.FakeEntity>("test").NotRaised<Fakes.FakeEvent>();
            act.Should().Throw<RaisedException>();
        }

    }
}
