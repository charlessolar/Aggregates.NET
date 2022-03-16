using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class FakeDomainEvent : IFullEvent
    {
        public class FakeEvent : IEvent { }

        public Guid? EventId { get; set; }

        public IEvent Event => new FakeEvent();

        public IEventDescriptor Descriptor => new EventDescriptor { StreamType = StreamTypes.Domain };
    }
    public class FakeNotHandledEvent : IFullEvent
    {
        public class UnknownEvent : IEvent { }

        public Guid? EventId { get; set; }

        public IEvent Event => new UnknownEvent();

        public IEventDescriptor Descriptor => new EventDescriptor { StreamType = StreamTypes.Domain };
    }



    public class FakeState : State<FakeState>
    {
        public int Handles = 0;
        public int Conflicts = 0;
        public bool TakeASnapshot = false;

        public bool ThrowDiscard = false;
        public bool ThrowAbandon = false;

        public bool RuleCheck = false;

        public bool SnapshotWasRestored { get; private set; }
        public bool WasSnapshotting { get; private set; }

        private void Handle(FakeDomainEvent.FakeEvent e)
        {
            Handles++;
        }

        protected override bool ShouldSnapshot()
        {
            return TakeASnapshot;
        }
        protected override void SnapshotRestored()
        {
            SnapshotWasRestored = true;
        }
        protected override void Snapshotting()
        {
            WasSnapshotting = true;
        }
    }
    public class FakeChildState : State<FakeChildState, FakeState> { }

    public class FakeEntity : Entity<FakeEntity, FakeState>
    {
        private FakeEntity() { }

        public void ApplyEvents<TEvent>(TEvent[] events) where TEvent : IEvent
        {
            foreach (var @event in events)
                (this as IEntity<FakeState>).Apply(@event);
        }

    }

    public class FakeChildEntity : Entity<FakeChildEntity, FakeChildState, FakeEntity>
    {
        private FakeChildEntity() { }

        public void ApplyEvents<TEvent>(TEvent[] events) where TEvent : IEvent
        {
            foreach (var @event in events)
                (this as IEntity<FakeChildState>).Apply(@event);
        }
    }

    public class FakeEnumeration : Enumeration<FakeEnumeration, int>
    {
        public static FakeEnumeration One = new FakeEnumeration(1, "one");
        public static FakeEnumeration Two = new FakeEnumeration(2, "two");

        public FakeEnumeration(int value, string displayName) : base(value, displayName) { }
    }

    public class FakeRepository : IRepository<FakeEntity>, IRepositoryCommit
    {
        public bool CommitCalled = false;
        public bool PrepareCalled = false;
        public bool DisposeCalled = false;
        public int FakeChangedStreams = 0;

        public int ChangedStreams => FakeChangedStreams;

        public Task Commit(Guid commitId, IDictionary<string, string> commitHeaders)
        {
            CommitCalled = true;
            return Task.CompletedTask;
        }
        public Task Prepare(Guid commitId)
        {
            PrepareCalled = true;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCalled = true;
        }

        public Task<FakeEntity> Get(Id id)
        {
            throw new NotImplementedException();
        }

        public Task<FakeEntity> Get(string bucket, Id id)
        {
            throw new NotImplementedException();
        }

        public Task<FakeEntity> New(Id id)
        {
            throw new NotImplementedException();
        }

        public Task<FakeEntity> New(string bucket, Id id)
        {
            throw new NotImplementedException();
        }


        public Task<FakeEntity> TryGet(Id id)
        {
            throw new NotImplementedException();
        }

        public Task<FakeEntity> TryGet(string bucket, Id id)
        {
            throw new NotImplementedException();
        }
    }
    public class FakeAppUnitOfWork : UnitOfWork.IUnitOfWork
    {
        public dynamic Bag { get; set; }
    }
    public class FakeMutator : IMutate
    {
        public bool MutatedIncoming = false;
        public bool MutatedOutgoing = false;
        public bool ShouldSucceed = false;

        public IMutating MutateIncoming(IMutating mutating)
        {
            MutatedIncoming = true;
            if (!ShouldSucceed)
                throw new Exception();
            return mutating;
        }

        public IMutating MutateOutgoing(IMutating mutating)
        {
            MutatedOutgoing = true;
            if (!ShouldSucceed)
                throw new Exception();
            return mutating;
        }
    }
}
