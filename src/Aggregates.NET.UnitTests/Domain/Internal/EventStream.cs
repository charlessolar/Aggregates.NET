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
        private Moq.Mock<IOobHandler> _handler;
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
            _handler = new Moq.Mock<IOobHandler>();
            _eventstore = new Moq.Mock<IStoreEvents>();

            _uow = new Moq.Mock<IUnitOfWork>();

            _event = new Moq.Mock<IFullEvent>();
            _event.Setup(x => x.Descriptor).Returns(new EventDescriptor());
            _events = new[]
            {
                _event.Object
            };

            _builder.Setup(x => x.Build<IStoreSnapshots>()).Returns(_snapshots.Object);
            _builder.Setup(x => x.Build<IOobHandler>()).Returns(_handler.Object);
            _builder.Setup(x => x.Build<IStoreEvents>()).Returns(_eventstore.Object);
            _builder.Setup(x => x.Build<IUnitOfWork>()).Returns(_uow.Object);


            _store.Setup(
                x => x.WriteStream<Entity>(Moq.It.IsAny<IEventStream>(), Moq.It.IsAny<IDictionary<string, string>>()));
            _snapshots.Setup(
                x =>
                    x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(),
                        Moq.It.IsAny<IDictionary<string, string>>()));
            _handler.Setup(x => x.Publish<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>()));
        }

        [Test]
        public void version()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);

            Assert.AreEqual(0, stream.CommitVersion);
            Assert.AreEqual(0, stream.StreamVersion);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(0, stream.CommitVersion);
            Assert.AreEqual(1, stream.StreamVersion);
        }

        [Test]
        public void version_with_snapshot()
        {
            var memento = new Moq.Mock<IMemento>();
            memento.Setup(x => x.EntityId).Returns("test");

            var snapshot = new Moq.Mock<ISnapshot>();
            snapshot.Setup(x => x.Version).Returns(1);
            snapshot.Setup(x => x.Payload).Returns(memento.Object);

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, snapshot.Object);

            Assert.AreEqual(1, stream.StreamVersion);
            Assert.AreEqual(1, stream.CommitVersion);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(2, stream.StreamVersion);
            Assert.AreEqual(1, stream.CommitVersion);
        }

        [Test]
        public void oob_uncomitted()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);

            stream.AddOutOfBand(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(1, stream.OobUncommitted.Count());
        }

        [Test]
        public void dirty_check()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);

            Assert.False(stream.Dirty);

            stream.AddOutOfBand(new FakeEvent(), new Dictionary<string, string>());

            Assert.False(stream.Dirty);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.True(stream.Dirty);
        }

        [Test]
        public void total_uncomitted()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);

            Assert.AreEqual(0, stream.TotalUncommitted);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(1, stream.TotalUncommitted);

            stream.AddOutOfBand(new FakeEvent(), new Dictionary<string, string>());

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

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, snapshot.Object);

            Assert.AreEqual(1, stream.StreamVersion);

            Assert.AreEqual(new Id("test"), stream.CurrentMemento.EntityId);

        }

        [Test]
        public void clone_has_important_info()
        {
            var memento = new Moq.Mock<IMemento>();
            memento.Setup(x => x.EntityId).Returns("test");

            var snapshot = new Moq.Mock<ISnapshot>();
            snapshot.Setup(x => x.Version).Returns(1);
            snapshot.Setup(x => x.Payload).Returns(memento.Object);

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, snapshot.Object);

            var clone = stream.Clone();

            Assert.AreEqual("test", clone.StreamType);
            Assert.AreEqual("test", clone.Bucket);
            Assert.AreEqual(new Id("test"), clone.StreamId);
            Assert.AreEqual("test", clone.StreamName);

            Assert.AreEqual(stream.CommitVersion, clone.CommitVersion);
            Assert.AreEqual(stream.CurrentMemento, clone.CurrentMemento);

        }

        [Test]
        public void concat_adds_to_committed()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);

            Assert.AreEqual(0, stream.CommitVersion);

            (stream as IEventStream).Concat(_events);

            Assert.AreEqual(1, stream.CommitVersion);
        }

        [Test]
        public void events_gets_events()
        {
            _eventstore.Setup(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new IFullEvent[] {}.AsEnumerable()));

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.Events(0, 1);

            _eventstore.Verify(x => x.GetEvents(Moq.It.IsAny<string>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>()),
                Moq.Times.Once);
        }
        [Test]
        public void oobevents_gets_oobevents()
        {
            _handler.Setup(x => x.Retrieve<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>(), Moq.It.IsAny<bool>()))
                .Returns(Task.FromResult(new IFullEvent[] { }.AsEnumerable()));

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.OobEvents(0, 1);

            _handler.Verify(x => x.Retrieve<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long?>(), Moq.It.IsAny<int?>(), Moq.It.IsAny<bool>()),
                Moq.Times.Once);
        }

        [Test]
        public void outgoing_event_mutator()
        {
            var mutator = new Moq.Mock<IEventMutator>();
            mutator.Setup(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>())).Returns<IMutating>(x => x);

            _builder.Setup(x => x.BuildAll<IEventMutator>()).Returns(new[] {mutator.Object});

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            mutator.Verify(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>()), Moq.Times.Once);
        }

        [Test]
        public void outgoing_event_mutator_adds_header()
        {
            var mutator = new Moq.Mock<IEventMutator>();
            mutator.Setup(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>())).Returns<IMutating>(x =>
            {
                x.Headers["test"] = "test";
                return x;
            });

            _builder.Setup(x => x.BuildAll<IEventMutator>()).Returns(new[] { mutator.Object });

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            mutator.Verify(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>()), Moq.Times.Once);

            Assert.True(stream.Uncommitted.ElementAt(0).Descriptor.Headers.ContainsKey("test"));
            Assert.AreEqual("test", stream.Uncommitted.ElementAt(0).Descriptor.Headers["test"]);
        }

        [Test]
        public void outgoing_oob_event_mutator()
        {
            var mutator = new Moq.Mock<IEventMutator>();
            mutator.Setup(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>())).Returns<IMutating>(x => x);

            _builder.Setup(x => x.BuildAll<IEventMutator>()).Returns(new[] { mutator.Object });

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.AddOutOfBand(new FakeEvent(), new Dictionary<string, string>());

            mutator.Verify(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>()), Moq.Times.Once);
        }
        [Test]
        public void outgoing_oob_event_mutator_adds_header()
        {
            var mutator = new Moq.Mock<IEventMutator>();
            mutator.Setup(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>())).Returns<IMutating>(x =>
            {
                x.Headers["test"] = "test";
                return x;
            });

            _builder.Setup(x => x.BuildAll<IEventMutator>()).Returns(new[] { mutator.Object });

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.AddOutOfBand(new FakeEvent(), new Dictionary<string, string>());

            mutator.Verify(x => x.MutateOutgoing(Moq.It.IsAny<IMutating>()), Moq.Times.Once);

            Assert.True(stream.OobUncommitted.ElementAt(0).Descriptor.Headers.ContainsKey("test"));
            Assert.AreEqual("test", stream.OobUncommitted.ElementAt(0).Descriptor.Headers["test"]);
        }

        [Test]
        public async Task commit_no_uncommitted()
        {

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);

            await stream.Commit(Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);


            _store.Verify(
                x => x.WriteStream<Entity>(Moq.It.IsAny<IEventStream>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
            _snapshots.Verify(
                x =>
                    x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(),
                        Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
            _handler.Verify(x => x.Publish<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
        }

        [Test]
        public async Task commit_with_events()
        {
            _store.Setup(
                    x => x.WriteStream<Entity>(Moq.It.IsAny<IEventStream>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);

            stream.Add(new FakeEvent(), new Dictionary<string, string>());
            await stream.Commit(Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);


            _store.Verify(
                x => x.WriteStream<Entity>(Moq.It.IsAny<IEventStream>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
            _snapshots.Verify(
                x =>
                    x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(),
                        Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
            _handler.Verify(x => x.Publish<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
        }

        [Test]
        public async Task commit_with_oob_events()
        {
            _handler.Setup(x => x.Publish<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                   Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                   Moq.It.IsAny<IDictionary<string, string>>())).Returns(Task.CompletedTask);


            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.AddOutOfBand(new FakeEvent(), new Dictionary<string, string>());

            await stream.Commit(Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);


            _store.Verify(
                x => x.WriteStream<Entity>(Moq.It.IsAny<IEventStream>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
            _snapshots.Verify(
                x =>
                    x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(),
                        Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
            _handler.Verify(x => x.Publish<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }

        [Test]
        public async Task commit_with_snapshot()
        {
            var memento = new Moq.Mock<IMemento>();
            memento.Setup(x => x.EntityId).Returns("test");

            _snapshots.Setup(
                x =>
                    x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(),
                        Moq.It.IsAny<IDictionary<string, string>>())).Returns(Task.CompletedTask);

            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.AddSnapshot(memento.Object);

            await stream.Commit(Guid.NewGuid(), new Dictionary<string, string>()).ConfigureAwait(false);


            _store.Verify(
                x => x.WriteStream<Entity>(Moq.It.IsAny<IEventStream>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
            _snapshots.Verify(
                x =>
                    x.WriteSnapshots<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                        Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IMemento>(),
                        Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
            _handler.Verify(x => x.Publish<Entity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(),
                Moq.It.IsAny<IEnumerable<Id>>(), Moq.It.IsAny<IEnumerable<IFullEvent>>(),
                Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
        }

        [Test]
        public void stream_flush_committed()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(0, stream.CommitVersion);
            Assert.AreEqual(1, stream.StreamVersion);
            Assert.AreEqual(1, stream.TotalUncommitted);

            stream.Flush(true);

            Assert.AreEqual(0, stream.TotalUncommitted);
            Assert.AreEqual(1, stream.CommitVersion);
        }
        [Test]
        public void stream_flush_not_committed()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.Add(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(0, stream.CommitVersion);
            Assert.AreEqual(1, stream.StreamVersion);
            Assert.AreEqual(1, stream.TotalUncommitted);

            stream.Flush(false);

            Assert.AreEqual(0, stream.TotalUncommitted);
            Assert.AreEqual(0, stream.CommitVersion);
        }

        [Test]
        public void stream_flush_with_oob_committed()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.AddOutOfBand(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(0, stream.CommitVersion);
            Assert.AreEqual(0, stream.StreamVersion);
            Assert.AreEqual(1, stream.TotalUncommitted);

            stream.Flush(true);

            Assert.AreEqual(0, stream.TotalUncommitted);
            Assert.AreEqual(0, stream.CommitVersion);
        }
        [Test]
        public void stream_flush_with_oob_not_committed()
        {
            var stream = new Aggregates.Internal.EventStream<Entity>(_builder.Object, _store.Object, "test", "test", "test", null, "test", _events, null);
            stream.AddOutOfBand(new FakeEvent(), new Dictionary<string, string>());

            Assert.AreEqual(0, stream.CommitVersion);
            Assert.AreEqual(0, stream.StreamVersion);
            Assert.AreEqual(1, stream.TotalUncommitted);

            stream.Flush(false);

            Assert.AreEqual(0, stream.TotalUncommitted);
            Assert.AreEqual(0, stream.CommitVersion);
        }
    }
}
