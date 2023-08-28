using Aggregates.Exceptions;
using Aggregates.Testing.TestableContext.Fakes;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Testing.TestableContext {
    public class ModelChecker : TestSubject<Aggregates.TestableContext> {

        [Fact]
        public async Task CreateModel() {

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId="test", CreateModel=true, Content = "test" }, Sut);

            Sut.App.Check<FakeModel>("test").Added();
        }
        [Fact]
        public async Task CreateModelWrongId() {

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", CreateModel = true, Content = "test" }, Sut);

            var act= () => Sut.App.Check<FakeModel>("test2").Added();
            act.Should().Throw<ModelException>();
        }
        [Fact]
        public async Task CreateModelCheckContent() {

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", CreateModel = true, Content = "test" }, Sut);

            Sut.App.Check<FakeModel>("test").Added(x => {
                return x.Content == "test";
            });
        }

        [Fact]
        public async Task CreateModelCheckContentFails() {

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", CreateModel = true, Content = "test" }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test").Added(x => {
                return x.Content == "test2";
            });
            act.Should().Throw<ModelException>();
        }

        [Fact]
        public async Task CreateModelCheckExactObject() {

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", CreateModel = true, Content = "test" }, Sut);

            Sut.App.Check<FakeModel>("test").Added(new FakeModel { EntityId = "test", Content = "test" });
        }
        [Fact]
        public async Task CreateModelCheckExactObjectFails() {

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", CreateModel = true, Content = "test" }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test").Added(new FakeModel { EntityId = "test", Content = "test2" });
            act.Should().Throw<ModelException>();
        }
        [Fact]
        public async Task DeleteModel() {

            Sut.App.Plan<FakeModel>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", DeleteModel = true }, Sut);

            Sut.App.Check<FakeModel>("test").Deleted();
        }
        [Fact]
        public async Task DeleteModelThatDoesntExistIsOk() {
            // Not up to us to determine if model should exist before deleting
            // some databases are ok with that
            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", DeleteModel = true }, Sut);

            Sut.App.Check<FakeModel>("test").Deleted();
        }
        [Fact]
        public async Task DeleteModelWrongId() {

            Sut.App.Plan<FakeModel>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test2", DeleteModel = true }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test").Deleted();
            act.Should().Throw<ModelException>();
        }

        [Fact]
        public async Task ReadModel() {

            Sut.App.Plan<FakeModel>("test").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", ReadModel = true }, Sut);

            Sut.App.Check<FakeModel>("test").Read();
        }
        [Fact]
        public async Task ReadModelWrongId() {

            Sut.App.Plan<FakeModel>("test2").Exists();

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test2", ReadModel = true }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test").Read();
            act.Should().Throw<ModelException>();
        }
        [Fact]
        public async Task ReadModelDoesntExist() {

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", ReadModel = true }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test").Read();
            act.Should().NotThrow();
        }

        [Fact]
        public async Task UpdateModel() {

            Sut.App.Plan<FakeModel>("test")
                .Exists(new FakeModel { EntityId = "test", Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", UpdateModel = true, Content="test2" }, Sut);

            Sut.App.Check<FakeModel>("test").Updated();
        }
        [Fact]
        public async Task UpdateModelIsUpdated() {

            Sut.App.Plan<FakeModel>("test")
                .Exists(new FakeModel { EntityId = "test", Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", UpdateModel = true, Content = "test2" }, Sut);

            Sut.App.Check<FakeModel>("test").Updated(x => {
                return x.Content == "test2";
            });
            Sut.App.Check<FakeModel>("test").Updated(new FakeModel { EntityId = "test", Content = "test2" });
        }
        [Fact]
        public async Task UpdateModelIsUpdatedFailAssert() {

            Sut.App.Plan<FakeModel>("test")
                .Exists(new FakeModel { EntityId = "test", Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", UpdateModel = true, Content = "test2" }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test").Updated(x => {
                return x.Content == "test";
            });
            act.Should().Throw<ModelException>();
        }
        [Fact]
        public async Task UpdateModelCheckWrongId() {

            Sut.App.Plan<FakeModel>("test")
                .Exists(new FakeModel { EntityId = "test", Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", UpdateModel = true, Content = "test2" }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test2").Updated();
            act.Should().Throw<ModelException>();
        }
        [Fact]
        public async Task UpdateModelIsUpdatedFailAssertWrongId() {

            Sut.App.Plan<FakeModel>("test")
                .Exists(new FakeModel { EntityId = "test", Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", UpdateModel = true, Content = "test2" }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test2").Updated(x => {
                return x.Content == "test";
            });
            act.Should().Throw<ModelException>();
        }
        [Fact]
        public async Task UpdateModelIsUpdatedWrongId() {

            Sut.App.Plan<FakeModel>("test")
                .Exists(new FakeModel { EntityId = "test", Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", UpdateModel = true, Content = "test2" }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test2").Updated(new FakeModel { EntityId = "test", Content = "test2" });
            act.Should().Throw<ModelException>();
        }
        [Fact]
        public async Task UpdateModelIsUpdatedWrongCheck() {

            Sut.App.Plan<FakeModel>("test")
                .Exists(new FakeModel { EntityId = "test", Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", UpdateModel = true, Content = "test2" }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test").Updated(new FakeModel { EntityId = "test", Content = "test3" });
            act.Should().Throw<ModelException>();
        }


        [Fact]
        public async Task UpdateModelWrongId() {

            Sut.App.Plan<FakeModel>("test")
                .Exists(new FakeModel { EntityId = "test", Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", UpdateModel = true, Content = "test2" }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test2").Read();
            act.Should().Throw<ModelException>();
        }
        [Fact]
        public async Task UpdateModelDoesntExist() {

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeEvent { EntityId = "test", ReadModel = true }, Sut);

            var act = () => Sut.App.Check<FakeModel>("test").Read();
            act.Should().NotThrow();
        }

    }
}
