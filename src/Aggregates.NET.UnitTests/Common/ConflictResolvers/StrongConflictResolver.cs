using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using NUnit.Framework;
using Aggregates.Messages;

namespace Aggregates.UnitTests.Common.ConflictResolvers
{
    [TestFixture]
    public class StrongConflictResolver
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

            private void Handle(FakeEvent e)
            {
                Handles++;
            }

            private void Conflict(FakeEvent e)
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

        [SetUp]
        public void Setup()
        {
            _snapstore = new Moq.Mock<IStoreSnapshots>();
            _eventstore = new Moq.Mock<IStoreEvents>();            

            _event = new Moq.Mock<IFullEvent>();
            _event.Setup(x => x.Event).Returns(new FakeEvent());
            _event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.Domain);
            
            _eventstore.Setup(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()))
                .Returns(Task.FromResult(0L));
        }
        

        [Test]
        public async Task strong_resolve_conflict()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_snapstore.Object, _eventstore.Object, streamGen);

            var entity = new FakeEntity();

            await resolver.Resolve<FakeEntity, FakeState>(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            Assert.AreEqual(1, entity.State.Conflicts);

            _eventstore.Verify(x => x.WriteEvents<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<IFullEvent[]>(), Moq.It.IsAny<IDictionary<string, string>>(), Moq.It.IsAny<long?>()), Moq.Times.Once);
            
        }

        [Test]
        public void no_route_exception()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_snapstore.Object, _eventstore.Object, streamGen);

            _event.Setup(x => x.Event).Returns(new FakeUnknownEvent());
            var entity = new FakeEntity();

            Assert.ThrowsAsync<ConflictResolutionFailedException>(
                () => resolver.Resolve<FakeEntity, FakeState>(entity, new[] {_event.Object}, Guid.NewGuid(), new Dictionary<string, string>()));
        }
        
        [Test]
        public void dont_catch_abandon_resolution()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_snapstore.Object, _eventstore.Object, streamGen);

            var entity = new FakeEntity();
            entity.State.ThrowAbandon = true;

            Assert.ThrowsAsync<AbandonConflictException>(
                () => resolver.Resolve<FakeEntity, FakeState>(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>()));
        }

        [Test]
        public async Task takes_snapshot()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_snapstore.Object, _eventstore.Object, streamGen);
            
            var entity = new FakeEntity();
            entity.State.TakeASnapshot = true;

            _snapstore.Setup(x => x.WriteSnapshots<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IState>(), Moq.It.IsAny<IDictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            await resolver.Resolve<FakeEntity, FakeState>(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            Assert.AreEqual(1, entity.State.Conflicts);

            _snapstore.Verify(x => x.WriteSnapshots<FakeEntity>(Moq.It.IsAny<string>(), Moq.It.IsAny<Id>(), Moq.It.IsAny<Id[]>(), Moq.It.IsAny<long>(), Moq.It.IsAny<IState>(), Moq.It.IsAny<IDictionary<string, string>>()), Moq.Times.Once);
            
        }

        [Test]
        public async Task oob_events_not_conflict_resolved()
        {
            var streamGen = new StreamIdGenerator((type, stream, bucket, id, parents) => "test");

            _event.Setup(x => x.Descriptor.Headers[Defaults.OobHeaderKey]).Returns("test");
            _event.Setup(x => x.Descriptor.StreamType).Returns(StreamTypes.OOB);

            // Runs all conflicting events back through a re-hydrated entity
            var resolver = new Aggregates.Internal.ResolveStronglyConflictResolver(_snapstore.Object, _eventstore.Object, streamGen);

            var entity = new FakeEntity();

            await resolver.Resolve<FakeEntity, FakeState>(entity, new[] { _event.Object }, Guid.NewGuid(), new Dictionary<string, string>())
                .ConfigureAwait(false);

            Assert.AreEqual(0, entity.State.Conflicts);
            
        }


    }
}
