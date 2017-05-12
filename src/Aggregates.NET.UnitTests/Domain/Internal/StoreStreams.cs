using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class StoreStreams
    {
        class Entity : Aggregate<Entity> { }

        class FakeEvent : IFullEvent
        {
            public Guid? EventId { get; }
            public object Event { get; }
            public IEventDescriptor Descriptor { get; }
        }

        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<ICache> _cache;
        private Moq.Mock<IEventStream> _stream;

        private Aggregates.Internal.StoreStreams _streamStore;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreEvents>();
            _cache = new Moq.Mock<ICache>();
            _stream = new Moq.Mock<IEventStream>();

            _streamStore = new Aggregates.Internal.StoreStreams(_builder.Object, _store.Object, _cache.Object, true,
                (a, b, c, d, e) => "test");
        }

        [Test]
        public async Task evict_stream()
        {
            _cache.Setup(x => x.Evict(Moq.It.IsAny<string>()));

            await _streamStore.Evict<Entity>(_stream.Object);

            _cache.Verify(x => x.Evict(Moq.It.IsAny<string>()), Moq.Times.Once);
        }

        [Test]
        public async Task cache_stream()
        {
            _cache.Setup(x => x.Cache(Moq.It.IsAny<string>(), Moq.It.IsAny<object>(), Moq.It.IsAny<bool>(),
                Moq.It.IsAny<bool>(), Moq.It.IsAny<bool>()));

            await _streamStore.Cache<Entity>(_stream.Object);

            _cache.Verify(x => x.Cache(Moq.It.IsAny<string>(), Moq.It.IsAny<object>(), Moq.It.IsAny<bool>(),
                Moq.It.IsAny<bool>(), Moq.It.IsAny<bool>()), Moq.Times.Once);
        }

        [Test]
        public void get_stream_is_cached()
        {
            _cache.Setup(x => x.Retreive(Moq.It.IsAny<string>())).Returns(_stream.Object);
            _store.Setup(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()));

            Assert.DoesNotThrowAsync(() => _streamStore.GetStream<Entity>("test", "test"));

            _store.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Never);
        }

        [Test]
        public async Task get_stream_not_cached()
        {
            _store.Setup(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>())).Returns(Task.FromResult(new IFullEvent[] {new FakeEvent()}.AsEnumerable()));
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));

            _cache.Setup(x => x.Retreive(Moq.It.IsAny<string>())).Returns((IEventStream)null);

            await _streamStore.GetStream<Entity>("test", "test");

            _store.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);
        }

        [Test]
        public async Task get_events()
        {
            _store.Setup(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>())).Returns(Task.FromResult(new IFullEvent[] { new FakeEvent() }.AsEnumerable()));

            await _streamStore.GetEvents<Entity>("test", "test");

            _store.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);
        }
        [Test]
        public async Task get_events_backwards()
        {
            _store.Setup(x => x.GetEventsBackwards(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>())).Returns(Task.FromResult(new IFullEvent[] { new FakeEvent() }.AsEnumerable()));

            await _streamStore.GetEventsBackwards<Entity>("test", "test");

            _store.Verify(x => x.GetEventsBackwards(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);
        }

        [Test]
        public async Task write_events_not_frozen()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>())).Returns(Task.FromResult(0L));

            await _streamStore.WriteStream<Entity>(_stream.Object, new Dictionary<string, string>());

            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>()), Moq.Times.Once);
        }

        [Test]
        public void write_events_frozen()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(true));

            Assert.ThrowsAsync<FrozenException>(
                () => _streamStore.WriteStream<Entity>(_stream.Object, new Dictionary<string, string>()));
        }
    }
}
