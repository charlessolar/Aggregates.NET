using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class StoreSnapshots
    {
        class Entity : IEventSource
        {
            public Id Id { get; set; }
            public long Version { get; set; }
            public IEventSource Parent { get; set; }
            public IEventStream Stream { get; set; }

            public void Hydrate(IEnumerable<IEvent> events) { }
        }


        private Moq.Mock<IMemento> _snapshot;
        private Moq.Mock<IStoreEvents> _store;
        private Moq.Mock<ISnapshotReader> _cache;
        private Moq.Mock<IFullEvent> _fullevent;

        private Aggregates.Internal.StoreSnapshots _snapshots;

        [SetUp]
        public void Setup()
        {
            _snapshot = new Moq.Mock<IMemento>();
            _store = new Moq.Mock<IStoreEvents>();
            _cache = new Moq.Mock<ISnapshotReader>();
            _fullevent = new Moq.Mock<IFullEvent>();

            _fullevent.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            _snapshots = new Aggregates.Internal.StoreSnapshots(_store.Object, null, (a, b, c, d, e) => "test");

            _snapshot.Setup(x => x.EntityId).Returns("test");
            _fullevent.Setup(x => x.Event).Returns(_snapshot.Object);
        }

        [Test]
        public async Task get_reads_last_event()
        {
            _store.Setup(x => x.GetEventsBackwards(Moq.It.IsAny<string>(), StreamPosition.End, Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new[] {_fullevent.Object}.AsEnumerable()));

            var snapshot = await _snapshots.GetSnapshot<Entity>("test", "test", null).ConfigureAwait(false);

            Assert.NotNull(snapshot);
            Assert.AreEqual(new Id("test"), snapshot.Payload.EntityId);
        }

        [Test]
        public async Task get_doesnt_exist()
        {
            var snapshot = await _snapshots.GetSnapshot<Entity>("test", "test", null).ConfigureAwait(false);
            Assert.Null(snapshot);
        }

        [Test]
        public async Task get_tries_cache()
        {
            _snapshots = new Aggregates.Internal.StoreSnapshots(_store.Object, _cache.Object, (a, b, c, d, e) => "test");

            _cache.Setup(x => x.Retreive("test")).Returns(Task.FromResult((ISnapshot)new Snapshot {Payload = _snapshot.Object}));

            var snapshot = await _snapshots.GetSnapshot<Entity>("test", "test", null).ConfigureAwait(false);
            
            Assert.NotNull(snapshot);
        }

        [Test]
        public async Task get_tries_cache_misses()
        {
            _snapshots = new Aggregates.Internal.StoreSnapshots(_store.Object, _cache.Object, (a, b, c, d, e) => "test");

            _cache.Setup(x => x.Retreive("test")).Returns(Task.FromResult((ISnapshot)null));

            var snapshot = await _snapshots.GetSnapshot<Entity>("test", "test", null).ConfigureAwait(false);

            Assert.Null(snapshot);
        }

        [Test]
        public async Task write_fills_out_descriptor()
        {
            IFullEvent saved = null;

            _store.Setup(
                    x =>
                        x.WriteSnapshot(Moq.It.IsAny<string>(), Moq.It.IsAny<IFullEvent>(),
                            Moq.It.IsAny<IDictionary<string, string>>()))
                            .Returns(Task.FromResult(0L))
                .Callback<string, IFullEvent, IDictionary<string, string>>(
                    (stream, events, headers) =>
                    {
                        saved = events;
                    });

            await _snapshots.WriteSnapshots<Entity>("test", "test", null, 0, _snapshot.Object, new Dictionary<string, string>()).ConfigureAwait(false);

            Assert.NotNull(saved);
            Assert.AreEqual(saved.Event, _snapshot.Object);
            Assert.AreEqual(new Id("test"), (saved.Event as IMemento).EntityId);

            var descriptor = saved.Descriptor;
            Assert.AreEqual(descriptor.EntityType, typeof(Entity).AssemblyQualifiedName);
            Assert.AreEqual(descriptor.StreamType, StreamTypes.Snapshot);
        }

    }
}
