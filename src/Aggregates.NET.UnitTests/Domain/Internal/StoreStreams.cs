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

        class EntityWithMemento : AggregateWithMemento<EntityWithMemento, EntityWithMemento.Memento>
        {
            public class Memento : IMemento
            {
                public Id EntityId { get; set; }
            }

            protected override void RestoreSnapshot(Memento memento)
            {
            }

            protected override Memento TakeSnapshot()
            {
                return new Memento {EntityId = Id};
            }

            protected override bool ShouldTakeSnapshot()
            {
                return false;
            }
        }

        class FakeEvent : IFullEvent
        {
            public Guid? EventId { get; set; }
            public object Event { get; set; }
            public IEventDescriptor Descriptor { get; set; }
        }

        private Moq.Mock<IMessagePublisher> _publisher;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<IStoreSnapshots> _snapstore;
        private Moq.Mock<ICache> _cache;
        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IEventMutator> _mutator;

        private Aggregates.Internal.StoreStreams _streamStore;

        [SetUp]
        public void Setup()
        {
            _publisher = new Moq.Mock<IMessagePublisher>();
            _store = new Moq.Mock<IStoreEvents>();
            _snapstore = new Moq.Mock<IStoreSnapshots>();
            _cache = new Moq.Mock<ICache>();
            _stream = new Moq.Mock<IEventStream>();
            _mutator = new Moq.Mock<IEventMutator>();

            _streamStore = new Aggregates.Internal.StoreStreams(_cache.Object, _store.Object, _publisher.Object, _snapstore.Object,(a, b, c, d, e) => "test", new IEventMutator[] {});
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

            await _streamStore.GetEvents<Entity>(_stream.Object, 0, 1);

            _store.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);
        }
        [Test]
        public async Task get_events_backwards()
        {
            _store.Setup(x => x.GetEventsBackwards(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>())).Returns(Task.FromResult(new IFullEvent[] { new FakeEvent() }.AsEnumerable()));

            await _streamStore.GetEventsBackwards<Entity>(_stream.Object, 0, 1);

            _store.Verify(x => x.GetEventsBackwards(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()), Moq.Times.Once);
        }

        [Test]
        public async Task write_events_not_frozen()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>())).Returns(Task.FromResult(0L));

            var @event = new Moq.Mock<IFullEvent>();
            @event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.Domain);

            _stream.Setup(x => x.Uncommitted).Returns(new IFullEvent[] { @event.Object }.AsEnumerable());

            await _streamStore.WriteStream<Entity>(Guid.NewGuid(), _stream.Object, new Dictionary<string, string>());

            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>()), Moq.Times.Once);
        }

        [Test]
        public void write_events_frozen()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(true));

            Assert.ThrowsAsync<FrozenException>(
                () => _streamStore.WriteStream<Entity>(Guid.NewGuid(), _stream.Object, new Dictionary<string, string>()));
        }

        [Test]
        public async Task get_stream_has_snapshot()
        {
            _store.Setup(x => x.GetEvents(Moq.It.IsAny<string>(), 2, Moq.It.IsAny<int?>())).Returns(Task.FromResult(new IFullEvent[] { new FakeEvent() }.AsEnumerable()));
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));

            var snapshot = new Moq.Mock<ISnapshot>();
            snapshot.Setup(x => x.Version).Returns(1);

            _snapstore.Setup(x => x.GetSnapshot<EntityWithMemento>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>())).Returns(Task.FromResult(snapshot.Object));

            _cache.Setup(x => x.Retreive(Moq.It.IsAny<string>())).Returns((IEventStream)null);

            var entity = await _streamStore.GetStream<EntityWithMemento>("test", "test");

            _store.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), 2, Moq.It.IsAny<int?>()), Moq.Times.Once);
            Assert.AreEqual(1, entity.CommitVersion);
        }
    }
}
