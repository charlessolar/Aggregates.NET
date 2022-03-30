using Aggregates.Contracts;
using FakeItEasy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Aggregates.Common
{
    public class EventSubscriber : TestSubject<Internal.EventSubscriber>
    {
        [Fact]
        public async Task SetupCreatesProjection()
        {
            var consumer = Fake<IEventStoreConsumer>();
            var messaging = Fake<IMessaging>();

            A.CallTo(() => messaging.GetHandledTypes()).Returns(new Type[] { typeof(FakeDomainEvent.FakeEvent) });

            await Sut.Setup("test", Version.Parse("1.0.0"));

            A.CallTo(() => consumer.SetupProjection("test", A<Version>.Ignored, A<Type[]>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task NoEventsDoesntCreateProjection()
        {
            var consumer = Fake<IEventStoreConsumer>();
            var messaging = Fake<IMessaging>();

            A.CallTo(() => messaging.GetHandledTypes()).Returns(new Type[] {  });

            await Sut.Setup("test", Version.Parse("1.0.0"));

            A.CallTo(() => consumer.SetupProjection("test", A<Version>.Ignored, A<Type[]>.Ignored)).MustNotHaveHappened();
        }
        [Fact]
        public async Task ConnectToProjection()
        {
            var consumer = Fake<IEventStoreConsumer>();
            var messaging = Fake<IMessaging>();

            A.CallTo(() => messaging.GetHandledTypes()).Returns(new Type[] { typeof(FakeDomainEvent.FakeEvent) });

            await Sut.Setup("test", Version.Parse("1.0.0"));
            await Sut.Connect();

            A.CallTo(() => consumer.ConnectToProjection("test", A<Version>.Ignored, A<IEventStoreConsumer.EventAppeared>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task DoesntConnectToProjection()
        {
            var consumer = Fake<IEventStoreConsumer>();
            var messaging = Fake<IMessaging>();

            A.CallTo(() => messaging.GetHandledTypes()).Returns(new Type[] { });

            await Sut.Setup("test", Version.Parse("1.0.0"));
            await Sut.Connect();

            A.CallTo(() => consumer.ConnectToProjection("test", A<Version>.Ignored, A<IEventStoreConsumer.EventAppeared>.Ignored)).MustNotHaveHappened();
        }

    }
}
