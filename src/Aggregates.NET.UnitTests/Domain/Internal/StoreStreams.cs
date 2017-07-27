using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;
using Aggregates.Extensions;

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
            snapshot.Setup(x => x.Version).Returns(2);

            _snapstore.Setup(x => x.GetSnapshot<EntityWithMemento>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>())).Returns(Task.FromResult(snapshot.Object));

            _cache.Setup(x => x.Retreive(Moq.It.IsAny<string>())).Returns((IEventStream)null);

            var entity = await _streamStore.GetStream<EntityWithMemento>("test", "test");

            _store.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), 2, Moq.It.IsAny<int?>()), Moq.Times.Once);
            Assert.AreEqual(2, entity.CommitVersion);
        }

        [Test]
        public async Task write_events_not_frozen()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>())).Returns(Task.FromResult(0L));

            var @event = new Moq.Mock<IFullEvent>();
            @event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.Domain);
            @event.Setup(x => x.Descriptor.Headers).Returns(new Dictionary<string, string>());

            _stream.Setup(x => x.Uncommitted).Returns(new IFullEvent[] { @event.Object }.AsEnumerable());

            await _streamStore.WriteStream<Entity>(Guid.NewGuid(), _stream.Object, new Dictionary<string, string>());

            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>()), Moq.Times.Once);
        }
        [Test]
        public async Task write_stream_not_dirty()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>())).Returns(Task.FromResult(0L));

            await _streamStore.WriteStream<Entity>(Guid.NewGuid(), _stream.Object, new Dictionary<string, string>());

            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>()), Moq.Times.Never);
            _store.Verify(
                x => x.WriteSnapshot(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent>(),
                    Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
        }

        [Test]
        public async Task write_stream_with_events_with_snapshots()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>())).Returns(Task.FromResult(0L));
            _snapstore.Setup(x => x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), 
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(), Moq.It.IsAny<IDictionary<string,string>>()))
                .Returns(Task.FromResult(0L));

            var @event = new Moq.Mock<IFullEvent>();
            @event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.Domain);
            @event.Setup(x => x.Descriptor.Headers).Returns(new Dictionary<string, string>());
            var snapshot = new Moq.Mock<IMemento>();
            
            _stream.Setup(x => x.Uncommitted).Returns(new IFullEvent[] { @event.Object }.AsEnumerable());
            _stream.Setup(x => x.PendingSnapshot).Returns(snapshot.Object);

            await _streamStore.WriteStream<Entity>(Guid.NewGuid(), _stream.Object, new Dictionary<string, string>());

            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>()), Moq.Times.Once);
            _snapstore.Verify(x => x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(),
                Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }

        [Test]
        public async Task write_stream_pending_oobs()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteMetadata(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<long?>(),
                Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<bool?>(), Moq.It.IsAny<Guid?>(),
                Moq.It.IsAny<bool>(), Moq.It.IsAny<IDictionary<string, string>>())).Returns(Task.CompletedTask);

            _stream.Setup(x => x.PendingOobs).Returns(new[] {new OobDefinition {Id = "test"}});
            
            await _streamStore.WriteStream<Entity>(Guid.NewGuid(), _stream.Object, new Dictionary<string, string>());

            _store.Verify(x => x.WriteMetadata(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<long?>(),
                Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<TimeSpan?>(), Moq.It.IsAny<bool?>(), Moq.It.IsAny<Guid?>(),
                Moq.It.IsAny<bool>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }

        [Test]
        public async Task write_stream_oob_events()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Returns(Task.FromResult(0L));

            var @event = new Moq.Mock<IFullEvent>();
            @event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.OOB);
            @event.Setup(x => x.Descriptor.Headers).Returns(new Dictionary<string, string> {[Defaults.OobHeaderKey] = "test"});

            _stream.Setup(x => x.Uncommitted).Returns(new IFullEvent[] { @event.Object }.AsEnumerable());
            _stream.Setup(x => x.Oobs).Returns(new[] {new OobDefinition {Id = "test"}});

            await _streamStore.WriteStream<Entity>(Guid.NewGuid(), _stream.Object, new Dictionary<string, string>());

            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
        }

        [Test]
        public async Task write_events_commit_id_incremented()
        {
            IEnumerable<IFullEvent> savedEvents = null;

            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>()))
                .Returns(Task.FromResult(0L))
                .Callback<string, IEnumerable<IFullEvent>, IDictionary<string,string>, long>((stream, events, headers, version) => savedEvents=events);

            var @event = new Moq.Mock<IFullEvent>();
            @event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.Domain);
            @event.Setup(x => x.Descriptor.Headers).Returns(new Dictionary<string, string>());

            _stream.Setup(x => x.Uncommitted).Returns(new IFullEvent[] { @event.Object, @event.Object }.AsEnumerable());

            var commitId = Guid.NewGuid();
            var expectedEventId1 = commitId;
            var expectedEventId2 = commitId.Increment();

            await _streamStore.WriteStream<Entity>(commitId, _stream.Object, new Dictionary<string, string>());

            Assert.NotNull(savedEvents);
            Assert.AreEqual(expectedEventId1, savedEvents.ElementAt(0).EventId);
            Assert.AreEqual(expectedEventId2, savedEvents.ElementAt(1).EventId);

            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long>()), Moq.Times.Once);
        }

        [Test]
        public void oob_stream_not_defined()
        {
            _store.Setup(x => x.IsFrozen(Moq.It.IsAny<string>())).Returns(Task.FromResult(false));
            _store.Setup(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>())).Returns(Task.FromResult(0L));

            var @event = new Moq.Mock<IFullEvent>();
            @event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.OOB);
            @event.Setup(x => x.Descriptor.Headers).Returns(new Dictionary<string, string> { [Defaults.OobHeaderKey] = "test" });

            _stream.Setup(x => x.Uncommitted).Returns(new IFullEvent[] { @event.Object }.AsEnumerable());
            //_stream.Setup(x => x.Oobs).Returns(new[] { new OobDefinition { Id = "test" } });

            Assert.ThrowsAsync<InvalidOperationException>(
                () => _streamStore.WriteStream<Entity>(Guid.NewGuid(), _stream.Object,
                    new Dictionary<string, string>()));

            _store.Verify(x => x.WriteEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Never);
        }
        
    }
}
