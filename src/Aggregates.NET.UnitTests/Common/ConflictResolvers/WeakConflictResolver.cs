using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using NUnit.Framework;
using Aggregates.Messages;

namespace Aggregates.UnitTests.Domain.Internal.ConflictResolvers
{
    [TestFixture]
    public class WeakConflictResolver
    {
        class FakeEvent : IEvent { }
        class FakeUnknownEvent : IEvent { }
        class FakeEntity : Aggregates.Entity<FakeEntity, FakeState>
        {
            public FakeEntity()
            {
                Id = "test";
                State = new FakeState();
            }

        }
        class FakeState : Aggregates.State<FakeState>
        {
            public Id EntityId { get; set; }
            public int Handles = 0;
            public int Conflicts = 0;
            public bool TakeASnapshot = false;

            public bool ThrowDiscard = false;
            public bool ThrowAbandon = false;

            private void Handle(IEvent e)
            {
                Handles++;
            }

            private void Conflict(IEvent e)
            {
                if (ThrowDiscard)
                    throw new DiscardEventException();
                if (ThrowAbandon)
                    throw new AbandonConflictException();

                Conflicts++;
            }
            protected override void RestoreSnapshot(FakeState snapshot)
            {
            }
            protected override bool ShouldSnapshot()
            {
                return TakeASnapshot;
            }
        }

        private Moq.Mock<IStoreSnapshots> _snapstore;
        private Moq.Mock<IStoreEvents> _eventstore;
        private Moq.Mock<IFullEvent> _event;
        private Moq.Mock<IDelayedChannel> _channel;
        private Moq.Mock<IDelayedMessage> _delayedEvent;

        [SetUp]
        public void Setup()
        {
            _snapstore = new Moq.Mock<IStoreSnapshots>();
            _eventstore = new Moq.Mock<IStoreEvents>();
            _channel = new Moq.Mock<IDelayedChannel>();
            _delayedEvent = new Moq.Mock<IDelayedMessage>();
            
            
            _event = new Moq.Mock<IFullEvent>();
            _event.Setup(x => x.Event).Returns(new FakeEvent());
            _event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.Domain);

            var conflictingEvent = new ConflictingEvents { Events = new[] { _event.Object } };
            _delayedEvent.Setup(x => x.Message).Returns(conflictingEvent);

            _channel.Setup(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()))
                .Returns(Task.FromResult((TimeSpan?)TimeSpan.FromSeconds(60)));


            _channel.Setup(x => x.Pull(Moq.It.IsAny<string>(), Moq.It.IsAny<string>(), Moq.It.IsAny<int?>()))
                .Returns(Task.FromResult(new[] { _delayedEvent.Object }.AsEnumerable()));

            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Returns(Task.FromResult(0L));
            
        }
        


        [Test]
        public Task weak_resolve_conflict()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");
            
            // Delays conflicting events to be resolved later
            var resolver = new Aggregates.Internal.ResolveWeaklyConflictResolver(_snapstore.Object, _eventstore.Object, _channel.Object, streamGen);

            var entity = new FakeEntity();
            return Task.CompletedTask;

            //await resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
            //    .ConfigureAwait(false);

            //Assert.AreEqual(1, entity.Conflicts);

            //_stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
            //    Moq.Times.Once);

            //_store.Verify(x => x.WriteStream<Entity>(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IEventStream>(),
            //    Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }

        [Test]
        public void no_route_exception()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Delayes conflicting events to be resolved later
            var resolver = new Aggregates.Internal.ResolveWeaklyConflictResolver(_snapstore.Object, _eventstore.Object, _channel.Object, streamGen);

            var entity = new FakeEntity();

            //_resolver.Setup(x => x.Conflict(Moq.It.IsAny<Entity>(), typeof(Event))).Throws<NoRouteException>();

            //Assert.ThrowsAsync<ConflictResolutionFailedException>(
            //    () => resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>()));
        }
        

        [Test]
        public void dont_catch_abandon_resolution()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Delayes conflicting events to be resolved later
            var resolver = new Aggregates.Internal.ResolveWeaklyConflictResolver(_snapstore.Object, _eventstore.Object, _channel.Object, streamGen);

            var entity = new FakeEntity();

            //_resolver.Setup(x => x.Conflict(Moq.It.IsAny<Entity>(), typeof(Event))).Throws<AbandonConflictException>();

            //Assert.ThrowsAsync<AbandonConflictException>(
            //    () => resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>()));
        }

        [Test]
        public Task takes_snapshot()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Delayes conflicting events to be resolved later
            var resolver = new Aggregates.Internal.ResolveWeaklyConflictResolver(_snapstore.Object, _eventstore.Object, _channel.Object, streamGen);
            
            var entity = new FakeEntity();
            entity.State.TakeASnapshot = true;

            return Task.CompletedTask;
            //await resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
            //    .ConfigureAwait(false);

            //Assert.AreEqual(1, entity.Conflicts);

            //_stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
            //    Moq.Times.Once);

            //_stream.Verify(x => x.AddSnapshot(Moq.It.IsAny<IMemento>()), Moq.Times.Once);

            //_store.Verify(x => x.WriteStream<Entity>(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IEventStream>(),
            //    Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
        }

        [Test]
        public Task conflict_no_resolve()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            _channel.Setup(x => x.Age(Moq.It.IsAny<string>(), Moq.It.IsAny<string>()))
                .Returns(Task.FromResult((TimeSpan?)TimeSpan.FromSeconds(1)));

            // Delays conflicting events to be resolved later
            var resolver = new Aggregates.Internal.ResolveWeaklyConflictResolver(_snapstore.Object, _eventstore.Object, _channel.Object, streamGen);

            var entity = new FakeEntity();

            return Task.CompletedTask;
            //await resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
            //    .ConfigureAwait(false);

            //Assert.AreEqual(0, entity.Conflicts);

            //_stream.Verify(x => x.Add(Moq.It.IsAny<IEvent>(), Moq.It.IsAny<IDictionary<string, string>>()),
            //    Moq.Times.Never);

            //_store.Verify(x => x.WriteStream<Entity>(Moq.It.IsAny<Guid>(), Moq.It.IsAny<IEventStream>(),
            //    Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Never);
        }
        [Test]
        public Task oob_events_not_conflict_resolved()
        {

            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            _event.Setup(x => x.Descriptor.Headers[Defaults.OobHeaderKey]).Returns("test");
            _event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.OOB);

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveWeaklyConflictResolver(_snapstore.Object, _eventstore.Object, _channel.Object, streamGen);

            var entity = new FakeEntity();

            return Task.CompletedTask;
            //await resolver.Resolve(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
            //    .ConfigureAwait(false);

            //Assert.AreEqual(0, entity.Conflicts);

            //_stream.Verify(x => x.AddOob(Moq.It.IsAny<IEvent>(), "test", Moq.It.IsAny<IDictionary<string, string>>()),
            //    Moq.Times.Once);
        }

    }
}
