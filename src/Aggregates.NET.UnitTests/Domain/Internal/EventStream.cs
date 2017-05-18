using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal
{
    [TestFixture]
    public class EventStream
    {
        class FakeEvent : IEvent { }
        class Entity : Aggregates.Aggregate<Entity> { }

        private Moq.Mock<IBuilder> _builder;
        private Moq.Mock<IStoreStreams> _store;

        private Moq.Mock<IStoreSnapshots> _snapshots;
        private Moq.Mock<IStoreEvents> _eventstore;

        private Moq.Mock<IUnitOfWork> _uow;

        private Moq.Mock<IFullEvent> _event;
        private IFullEvent[] _events;

        [SetUp]
        public void Setup()
        {
            _builder = new Moq.Mock<IBuilder>();
            _store = new Moq.Mock<IStoreStreams>();

            _snapshots = new Moq.Mock<IStoreSnapshots>();
            _eventstore = new Moq.Mock<IStoreEvents>();

            _uow = new Moq.Mock<IUnitOfWork>();

            _event = new Moq.Mock<IFullEvent>();
            _event.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            _events = new[]
            {
                _event.Object
            };

            _builder.Setup(x => x.Build<IStoreSnapshots>()).Returns(_snapshots.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_eventstore.Object);
            _builder.Setup(x => x.Build<IUnitOfWork>()).Returns(_uow.Object);


            _store.Setup(
                x => x.WriteStream<Entity>(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IEventStream>(), Moq.It.IsAny<IDictionary<string, string>>()));
            _snapshots.Setup(
                x =>
                    x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(),
                        Moq.It.IsAny<IDictionary<string, string>>()));
        }

        [Test]
        public void version()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, null);

            Assert.AreEqual(0, stream.CommitVersion);
            Assert.AreEqual(0, stream.StreamVersion);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(0, stream.CommitVersion);
            Assert.AreEqual(1, stream.StreamVersion);
        }

        [Test]
        public void new_stream_version()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, new IFullEvent[] {}, null);

            Assert.AreEqual(-1, stream.CommitVersion);
            Assert.AreEqual(-1, stream.StreamVersion);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(-1, stream.CommitVersion);
            Assert.AreEqual(0, stream.StreamVersion);
        }

        [Test]
        public void version_with_snapshot()
        {
            var memento = new Moq.Mock<IMemento>();
            memento.Setup(x => x.EntityId).Returns("test");

            var snapshot = new Moq.Mock<ISnapshot>();
            snapshot.Setup(x => x.Version).Returns(1);
            snapshot.Setup(x => x.Payload).Returns(memento.Object);

            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, snapshot.Object);

            Assert.AreEqual(1, stream.StreamVersion);
            Assert.AreEqual(1, stream.CommitVersion);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(2, stream.StreamVersion);
            Assert.AreEqual(1, stream.CommitVersion);
        }

        [Test]
        public void oob_no_define_throws()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, null);

            Assert.Throws<InvalidOperationException>(
                () => stream.AddOob(new FakeEvent(), "test", new Dictionary<string, string>()));
            
        }

        [Test]
        public void oob_uncomitted()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, null);

            stream.DefineOob("test");
            stream.AddOob(new FakeEvent(), "test", new Dictionary<string, string>());

            Assert.AreEqual(1, stream.Uncommitted.Count());
        }

        [Test]
        public void dirty_check()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, null);

            Assert.False(stream.Dirty);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.True(stream.Dirty);
        }
        [Test]
        public void oob_dirty_check()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, null);

            Assert.False(stream.Dirty);

            stream.DefineOob("test");
            stream.AddOob(new FakeEvent(), "test", new Dictionary<string, string>());

            Assert.True(stream.Dirty);
        }

        [Test]
        public void total_uncomitted()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, null);

            Assert.AreEqual(0, stream.TotalUncommitted);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(1, stream.TotalUncommitted);

            stream.DefineOob("test");
            stream.AddOob(new FakeEvent(), "test", new Dictionary<string, string>());

            Assert.AreEqual(2, stream.TotalUncommitted);

            var memento = new Moq.Mock<IMemento>();
            stream.AddSnapshot(memento.Object);

            Assert.AreEqual(3, stream.TotalUncommitted);
        }

        [Test]
        public void current_memento()
        {
            var memento = new Moq.Mock<IMemento>();
            memento.Setup(x => x.EntityId).Returns("test");

            var snapshot = new Moq.Mock<ISnapshot>();
            snapshot.Setup(x => x.Version).Returns(1);
            snapshot.Setup(x => x.Payload).Returns(memento.Object);

            // _events contains 1 event
            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, snapshot.Object);

            Assert.AreEqual(1, stream.StreamVersion);

            Assert.AreEqual(new Id("test"), stream.Snapshot.Payload.EntityId);

        }

        [Test]
        public void clone_has_important_info()
        {
            var memento = new Moq.Mock<IMemento>();
            memento.Setup(x => x.EntityId).Returns("test");

            var snapshot = new Moq.Mock<ISnapshot>();
            snapshot.Setup(x => x.Version).Returns(1);
            snapshot.Setup(x => x.Payload).Returns(memento.Object);

            var stream = new Aggregates.Internal.EventStream<Entity>("test", "test", null, null, _events, snapshot.Object);

            var clone = stream.Clone();
            
            Assert.AreEqual("test", clone.Bucket);
            Assert.AreEqual(new Id("test"), clone.StreamId);

            Assert.AreEqual(stream.CommitVersion, clone.CommitVersion);
            Assert.AreEqual(stream.Snapshot.Payload, clone.Snapshot.Payload);

        }
        
        
        
    }
}
