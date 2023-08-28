using Aggregates.Exceptions;
using Aggregates.Testing.TestableContext.Fakes;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Testing.TestableContext {
    public class ServiceChecker : TestSubject<Aggregates.TestableContext> {

        [Fact]
        public async Task CallService() {

            Sut.Processor.Plan<FakeService,FakeResponse>(new FakeService()).Response(new FakeResponse { Content = "test" });

            var handler = new FakeMessageHandler();
            var response = await handler.QueryService(new FakeCommand { EntityId = "test" }, Sut);

            response.Content.Should().Be("test");
            Sut.Processor.Check<FakeService, FakeResponse>(new FakeService()).Requested();
        }
        [Fact]
        public async Task CallServiceNotPlanned() {

            var handler = new FakeMessageHandler();
            var act = () => handler.QueryService(new FakeCommand { EntityId = "test" }, Sut);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task WasntRequested() {
            Sut.UoW.Plan<Fakes.FakeEntity>("test").Exists();
            Sut.Processor.Plan<FakeService, FakeResponse>(new FakeService()).Response(new FakeResponse { Content = "test" });

            var handler = new FakeMessageHandler();
            await handler.Handle(new FakeCommand { EntityId = "test" }, Sut);

            var act = () => Sut.Processor.Check<FakeService, FakeResponse>(new FakeService()).Requested();
            act.Should().Throw<ServiceException>();
        }
    }
}
