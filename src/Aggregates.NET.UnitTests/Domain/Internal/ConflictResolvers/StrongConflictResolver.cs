using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using NServiceBus;
using NUnit.Framework;

namespace Aggregates.NET.UnitTests.Domain.Internal.ConflictResolvers
{
    [TestFixture]
    public class StrongConflictResolver
    {
        class Entity : Aggregates.AggregateWithMemento<Entity, Entity.Memento>
        {
            public int Handles = 0;
            public int Conflicts = 0;
            public bool TakeASnapshot = false;

            public Entity(IEventStream stream, IRouteResolver resolver)
            {
                (this as INeedStream).Stream = stream;
                (this as INeedRouteResolver).Resolver = resolver;
            }

            public void Handle(IEvent e)
            {
                Handles++;
            }

            public void Conflict(IEvent e)
            {
                Conflicts++;
            }

            public class Memento : IMemento
            {
                public Id EntityId { get; set; }
            }

            protected override void RestoreSnapshot(Memento memento)
            {
            }

            protected override Memento TakeSnapshot()
            {
                return new Memento();
            }

            protected override bool ShouldTakeSnapshot()
            {
                return TakeASnapshot;
            }
        }
        class Event : IEvent { }

        private Moq.Mock<IEventStream> _stream;
        private Moq.Mock<IRouteResolver> _resolver;
        private Moq.Mock<IStoreEvents> _eventstore;
        private Moq.Mock<IStoreStreams> _store;
        private Moq.Mock<IFullEvent> _event;
        private bool _wasFrozen;

        [SetUp]
        public void Setup()
        {
            _stream = new Moq.Mock<IEventStream>();
            _resolver = new Moq.Mock<IRouteResolver>();
            _eventstore = new Moq.Mock<IStoreEvents>();
            _store = new Moq.Mock<IStoreStreams>();

            _resolver.Setup(x => x.Conflict(Moq.It.IsAny<Entity>(), typeof(Event)))
                .Returns((entity, e) => (entity as Entity).Conflict((IEvent) e));
            _resolver.Setup(x => x.Resolve(Moq.It.IsAny<Entity>(), typeof(Event)))
                .Returns((entity, e) => (entity as Entity).Handle((IEvent)e));

            _stream.Setup(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()));

            _event = new Moq.Mock<IFullEvent>();
            _event.Setup(x => x.Event).Returns(new Event());
            _event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.Domain);

            _eventstore.Setup(
                    x => x.WriteEvents("test", new[] { _event.Object }, Moq.It.IsAny<IDictionary<string, string>>(), null))
                .Returns(Task.FromResult(0L));

            _store.Setup(x => x.WriteStream<Entity>(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IEventStream>(),
                Moq.It.IsAny<IDictionary<string, string>>())).Returns(Task.CompletedTask);
            _store.Setup(x => x.Freeze<Entity>(Moq.It.IsAny<IEventStream>())).Returns(Task.CompletedTask).Callback(() => _wasFrozen = true);
            _store.Setup(x => x.Unfreeze<Entity>(Moq.It.IsAny<IEventStream>())).Returns(Task.CompletedTask);
        }

        [TearDown]
        public void Teardown()
        {
            // Verify stream is always unfrozen
            if (_wasFrozen)
                _store.Verify(x => x.Unfreeze<Entity>(Moq.It.IsAny<IEventStream>()), Moq.Times.Once);
        }

        [Test]
        public async Task strong_resolve_conflict()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_store.Object, _eventstore.Object, streamGen);

            var entity = new Entity(_stream.Object, _resolver.Object);

            await resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            Assert.AreEqual(1, entity.Conflicts);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);

            _store.Verify(
                x => x.WriteStream<Entity>(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IEventStream>(),
                    Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
        }

        [Test]
        public void no_route_exception()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_store.Object, _eventstore.Object, streamGen);

            var entity = new Entity(_stream.Object, _resolver.Object);

            _resolver.Setup(x => x.Conflict(Moq.It.IsAny<Entity>(), typeof(Event))).Throws<NoRouteException>();

            Assert.ThrowsAsync<ConflictResolutionFailedException>(
                () => resolver.Resolve(entity, new[] {_event.Object}, Guid.NewGuid(), new Dictionary<string, string>()));
        }

        [Test]
        public void dont_catch_frozen_exception()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_store.Object, _eventstore.Object, streamGen);

            var entity = new Entity(_stream.Object, _resolver.Object);

            _store.Setup(x => x.Freeze<Entity>(Moq.It.IsAny<IEventStream>())).Throws(new VersionException("test"));

            Assert.ThrowsAsync<VersionException>(
                () => resolver.Resolve(entity, Enumerable.Repeat(_event.Object, 51), Guid.NewGuid(), new Dictionary<string, string>()));
        }
        [Test]
        public async Task dont_freeze_less_than_50_conflicts()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_store.Object, _eventstore.Object, streamGen);

            var entity = new Entity(_stream.Object, _resolver.Object);

            _store.Setup(x => x.Freeze<Entity>(Moq.It.IsAny<IEventStream>())).Throws(new VersionException("test"));

            await resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            Assert.AreEqual(1, entity.Conflicts);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);

            _store.Verify(x => x.Freeze<Entity>(Moq.It.IsAny<IEventStream>()), Moq.Times.Never);
        }

        [Test]
        public void dont_catch_abandon_resolution()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_store.Object, _eventstore.Object, streamGen);

            var entity = new Entity(_stream.Object, _resolver.Object);

            _resolver.Setup(x => x.Conflict(Moq.It.IsAny<Entity>(), typeof(Event))).Throws<AbandonConflictException>();

            Assert.ThrowsAsync<AbandonConflictException>(
                () => resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>()));
        }

        [Test]
        public async Task takes_snapshot()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_store.Object, _eventstore.Object, streamGen);

            _stream.Setup(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()));
            _stream.Setup(x => x.StreamVersion).Returns(0);
            _stream.Setup(x => x.CommitVersion).Returns(1);

            var entity = new Entity(_stream.Object, _resolver.Object);
            entity.TakeASnapshot = true;

            await resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            Assert.AreEqual(1, entity.Conflicts);

            _stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);

            _store.Verify(
                x => x.WriteStream<Entity>(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IEventStream>(),
                    Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
            _stream.Verify(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()), Moq.Times.Once);
        }

        [Test]
        public async Task oob_events_not_conflict_resolved()
        {

            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            _event.Setup(x => x.Descriptor.Headers[Defaults.OobHeaderKey]).Returns("test");
            _event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.OOB);

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_store.Object, _eventstore.Object, streamGen);

            var entity = new Entity(_stream.Object, _resolver.Object);

            await resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            Assert.AreEqual(0, entity.Conflicts);

            _stream.Verify(x => x.AddOob(Moq.It.IsAny<IEvent>(), "test", Moq.It.IsAny<IDictionary<string, string>>()),
                Moq.Times.Once);
        }


    }
}
