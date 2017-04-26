using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class StorePocos
    {
        class Poco
        {
            public string Foo;
        }

        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<ICache> _cache;
        private IEnumerable<IFullEvent> _events;

        private Aggregates.Internal.StorePocos _pocoStore;

        [SetUp]
        public void Setup()
        {
            _store = new Moq.Mock<IStoreEvents>();
            _cache = new Moq.Mock<ICache>();

            _pocoStore = new Aggregates.Internal.StorePocos(_store.Object, _cache.Object, false, (a, b, c, d, e) => "test");

            var @event = new Moq.Mock<IFullEvent>();
            @event.Setup(x => x.Event).Returns(new Poco());
            @event.Setup(x => x.Descriptor.Version).Returns(0);
            _events = new[]
            {
                @event.Object
            };
        }

        [Test]
        public async Task get_reads_last_event()
        {
            _store.Setup(x => x.GetEventsBackwards(Moq.It.IsAny<string>(), StreamPosition.End, Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(_events));

            var poco = await _pocoStore.Get<Poco>("test", "test", null).ConfigureAwait(false);

            Assert.AreEqual(0, poco.Item1);
            Assert.NotNull(poco.Item2);
        }

        [Test]
        public async Task get_doesnt_exist()
        {
            var poco = await _pocoStore.Get<Poco>("test", "test", null).ConfigureAwait(false);
            Assert.Null(poco);
        }

        [Test]
        public async Task get_tries_cache_is_deep_copy()
        {
            _pocoStore = new Aggregates.Internal.StorePocos(_store.Object, _cache.Object, true, (a, b, c, d, e) => "test");

            _cache.Setup(x => x.Retreive("test")).Returns(new Tuple<long, Poco>(0, new Poco()));
            var poco = await _pocoStore.Get<Poco>("test", "test", null).ConfigureAwait(false);

            Assert.NotNull(poco.Item2);

            poco.Item2.Foo = "test";

            // Verify the change we made above is not reflected in the cache immediately 
            // (a unit of work save should update or evict cache)
            var poco2 = await _pocoStore.Get<Poco>("test", "test", null).ConfigureAwait(false);

            Assert.NotNull(poco2.Item2);
            Assert.True(string.IsNullOrEmpty(poco2.Item2.Foo));
        }

        [Test]
        public async Task get_tries_cache_misses()
        {
            _pocoStore = new Aggregates.Internal.StorePocos(_store.Object, _cache.Object, true, (a, b, c, d, e) => "test");

            _cache.Setup(x => x.Retreive("test")).Returns(() => null);
            var poco = await _pocoStore.Get<Poco>("test", "test", null).ConfigureAwait(false);

            Assert.Null(poco);
        }

        [Test]
        public async Task write_fills_out_descriptor()
        {
            var poco = new Poco {Foo = "test"};
            IEnumerable<IFullEvent> savedEvents=null;

            _store.Setup(
                    x =>
                        x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                            Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                            .Returns(Task.FromResult(0L))
                .Callback<string, IEnumerable<IFullEvent>, IDictionary<string, string>, long?>(
                    (stream, events, headers, version) =>
                    {
                        savedEvents = events;
                    });

            await _pocoStore.Write<Poco>(new Tuple<long, Poco>(0, poco), "test", "test", null,
                new Dictionary<string, string>()).ConfigureAwait(false);

            Assert.NotNull(savedEvents);
            var dto = savedEvents.First();
            Assert.AreEqual(dto.Event, poco);
            Assert.AreEqual((dto.Event as Poco).Foo, "test");

            var descriptor = dto.Descriptor;
            Assert.AreEqual(descriptor.EntityType, typeof(Poco).AssemblyQualifiedName);
            Assert.AreEqual(descriptor.StreamType, StreamTypes.Poco);
        }

    }
}
