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
    public class OobWriter : TestSubject<Internal.OobWriter>
    {
        [Fact]
        public async Task ShouldReadSizeFromEventStream()
        {
            var store = Fake<IStoreEvents>();
            Inject(store);

            await Sut.GetSize<FakeEntity>("test", "test", new Id[] { }, "test").ConfigureAwait(false);

            A.CallTo(() => store.Size(A<string>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldGetEventsFromEventStream()
        {
            var store = Fake<IStoreEvents>();
            Inject(store);

            await Sut.GetEvents<FakeEntity>("test", "test", new Id[] { }, "test").ConfigureAwait(false);

            A.CallTo(() => store.GetEvents(A<string>.Ignored, A<long?>.Ignored, A<int?>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldGetEventsBackwardsFromEventStream()
        {
            var store = Fake<IStoreEvents>();
            Inject(store);

            await Sut.GetEventsBackwards<FakeEntity>("test", "test", new Id[] { }, "test").ConfigureAwait(false);

            A.CallTo(() => store.GetEventsBackwards(A<string>.Ignored, A<long?>.Ignored, A<int?>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldAllowSavingNoEvents()
        {
            await Sut.WriteEvents<FakeEntity>("test", "test", new Id[] { }, new IFullEvent[] { }, Guid.NewGuid(), new Dictionary<string, string> { }).ConfigureAwait(false);
        }
        [Fact]
        public async Task ShouldSaveDurableEventsToEventStream()
        {
            var store = Fake<IStoreEvents>();
            Inject(store);
            var @event = Fake<IFullEvent>();
            A.CallTo(() => @event.Descriptor.Headers).Returns(new Dictionary<string, string>
            {
                [Defaults.OobHeaderKey] = "test",
                [Defaults.OobTransientKey] = "False"
            });
            Inject(@event);

            await Sut.WriteEvents<FakeEntity>("test", "test", new Id[] { }, Many<IFullEvent>(), Guid.NewGuid(), new Dictionary<string, string> { }).ConfigureAwait(false);

            A.CallTo(() => store.WriteEvents(A<string>.Ignored, A<IFullEvent[]>.Ignored, A<IDictionary<string, string>>.Ignored, A<long?>.Ignored)).MustHaveHappened();

        }
        [Fact]
        public async Task ShouldPublishTransientEvents()
        {
            var publisher = Fake<IMessageDispatcher>();
            Inject(publisher);
            var @event = Fake<IFullEvent>();
            A.CallTo(() => @event.Descriptor.Headers).Returns(new Dictionary<string, string>
            {
                [Defaults.OobHeaderKey] = "test",
                [Defaults.OobTransientKey] = "True"
            });
            Inject(@event);

            await Sut.WriteEvents<FakeEntity>("test", "test", new Id[] { }, Many<IFullEvent>(), Guid.NewGuid(), new Dictionary<string, string> { }).ConfigureAwait(false);

            A.CallTo(() => publisher.Publish(A<IFullMessage[]>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldNotSaveTransientEventsToEventStream()
        {
            var store = Fake<IStoreEvents>();
            Inject(store);
            var @event = Fake<IFullEvent>();
            A.CallTo(() => @event.Descriptor.Headers).Returns(new Dictionary<string, string>
            {
                [Defaults.OobHeaderKey] = "test",
                [Defaults.OobTransientKey] = "True"
            });
            Inject(@event);

            await Sut.WriteEvents<FakeEntity>("test", "test", new Id[] { }, Many<IFullEvent>(), Guid.NewGuid(), new Dictionary<string, string> { }).ConfigureAwait(false);

            A.CallTo(() => store.WriteEvents(A<string>.Ignored, A<IFullEvent[]>.Ignored, A<IDictionary<string, string>>.Ignored, A<long?>.Ignored)).MustNotHaveHappened();
        }
        [Fact]
        public async Task ShouldNotPublishDurableEvents()
        {
            var publisher = Fake<IMessageDispatcher>();
            Inject(publisher);
            var @event = Fake<IFullEvent>();
            A.CallTo(() => @event.Descriptor.Headers).Returns(new Dictionary<string, string>
            {
                [Defaults.OobHeaderKey] = "test",
                [Defaults.OobTransientKey] = "False"
            });
            Inject(@event);

            await Sut.WriteEvents<FakeEntity>("test", "test", new Id[] { }, Many<IFullEvent>(), Guid.NewGuid(), new Dictionary<string, string> { }).ConfigureAwait(false);

            A.CallTo(() => publisher.Publish(A<IFullMessage[]>.That.IsEmpty())).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldDefineDaysToLiveMetadata()
        {
            var store = Fake<IStoreEvents>();
            Inject(store);
            var @event = Fake<IFullEvent>();
            A.CallTo(() => @event.Descriptor.Headers).Returns(new Dictionary<string, string>
            {
                [Defaults.OobHeaderKey] = "test",
                [Defaults.OobTransientKey] = "False",
                [Defaults.OobDaysToLiveKey] = "1"
            });
            Inject(@event);

            await Sut.WriteEvents<FakeEntity>("test", "test", new Id[] { }, Many<IFullEvent>(), Guid.NewGuid(), new Dictionary<string, string> { }).ConfigureAwait(false);

            A.CallTo(() => store.WriteMetadata(A<string>.Ignored, A<long?>.Ignored, A<long?>.Ignored, TimeSpan.FromDays(1), A<TimeSpan?>.Ignored, A<bool>.Ignored, A<Dictionary<string,string>>.Ignored)).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldAddCommitHeadersToDescriptor()
        {
            var publisher = Fake<IMessageDispatcher>();
            Inject(publisher);
            var @event = Fake<IFullEvent>();
            A.CallTo(() => @event.Descriptor.Headers).Returns(new Dictionary<string, string>
            {
                [Defaults.OobHeaderKey] = "test",
                [Defaults.OobTransientKey] = "True"
            });
            Inject(@event);

            await Sut.WriteEvents<FakeEntity>("test", "test", new Id[] { }, Many<IFullEvent>(), Guid.NewGuid(), new Dictionary<string, string> {
                ["Test"] = "test"
            }).ConfigureAwait(false);

            A.CallTo(() => publisher.Publish(A<IFullMessage[]>.That.Matches(x => x[0].Headers.ContainsKey("Test")))).MustHaveHappened();
        }
        [Fact]
        public async Task ShouldWriteEmptyCommitHeaders()
        {
            var store = Fake<IStoreEvents>();
            Inject(store);
            var @event = Fake<IFullEvent>();
            A.CallTo(() => @event.Descriptor.Headers).Returns(new Dictionary<string, string>
            {
                [Defaults.OobHeaderKey] = "test",
                [Defaults.OobTransientKey] = "False"
            });
            Inject(@event);

            await Sut.WriteEvents<FakeEntity>("test", "test", new Id[] { }, Many<IFullEvent>(), Guid.NewGuid(), new Dictionary<string, string> {
                ["Test"] = "Test"
            }).ConfigureAwait(false);

            A.CallTo(() => store.WriteEvents(A<string>.Ignored, A<IFullEvent[]>.Ignored, A<IDictionary<string, string>>.That.IsEmpty(), A<long?>.Ignored)).MustHaveHappened();

        }

    }
}
