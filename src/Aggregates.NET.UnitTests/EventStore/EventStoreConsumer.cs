using Aggregates.Contracts;
using FakeItEasy;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.EventStore
{
    public class EventStoreConsumer : TestSubject<Internal.EventStoreConsumer>
    {
        [Fact]
        public async Task SetupProjectionAllEvents()
        {
            var settings = Fake<ISettings>();
            var client = Fake<IEventStoreClient>();
            A.CallTo(() => settings.AllEvents).Returns(true);

            await Sut.SetupProjection("test", new Version("1.0.0"), new Type[] { });

            A.CallTo(() => client.CreateProjection(A<string>.Ignored, A<string>.That.Contains("$any:"))).MustHaveHappened();
        }

        [Fact]
        public async Task SetupProjectionNoEvents()
        {
            var settings = Fake<ISettings>();
            var client = Fake<IEventStoreClient>();
            A.CallTo(() => settings.AllEvents).Returns(false);

            await Sut.SetupProjection("test", new Version("1.0.0"), new Type[] { });

            A.CallTo(() => client.CreateProjection(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }
        [Fact]
        public async Task SetupProjectionWithEvents()
        {
            var settings = Fake<ISettings>();
            var client = Fake<IEventStoreClient>();
            var registrar = Fake<IVersionRegistrar>();
            A.CallTo(() => settings.AllEvents).Returns(false);
            A.CallTo(() => registrar.GetVersionedName(A<Type>.Ignored)).Returns("xxx");

            await Sut.SetupProjection("test", new Version("1.0.0"), new Type[] { typeof(FakeDomainEvent.FakeEvent) });

            A.CallTo(() => client.CreateProjection(A<string>.Ignored, A<string>.That.Contains("'xxx':"))).MustHaveHappened();
        }
        [Fact]
        public async Task SetupChildrenProjection()
        {
            var client = Fake<IEventStoreClient>();

            await Sut.SetupChildrenProjection("test", new Version("1.0.0"));

            A.CallTo(() => client.CreateProjection(A<string>.That.Contains("children"), A<string>.Ignored)).MustHaveHappened();
            A.CallTo(() => client.CreateProjection(A<string>.That.Contains("1.0"), A<string>.Ignored)).MustHaveHappened();
        }
    }
}
