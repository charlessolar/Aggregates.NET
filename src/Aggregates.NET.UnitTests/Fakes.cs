using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using Aggregates.Messages;
using Aggregates.UnitOfWork.Query;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates
{
    public class FakeDomainEvent : IFullEvent
    {
        [Versioned("FakeEvent", "Fakes", 100)]
        public class FakeEvent : IEvent { }
		[Versioned("FakeEvent", "Fakes", 100)]
		public class FakeDuplicateEvent : IEvent { }
		[Versioned("FakeEvent", "Fakes", 99)]
        public class FakeOldEvent : IEvent { }

        public Guid? EventId { get; set; }

        public IEvent Event => new FakeEvent();
        public string EventType => "FakeEvent";

        public IEventDescriptor Descriptor => new EventDescriptor { StreamType = StreamTypes.Domain };
    }
    public class FakeNotHandledEvent : IFullEvent
    {
        public class UnknownEvent : IEvent { }

        public Guid? EventId { get; set; }

        public IEvent Event => new UnknownEvent();
		public string EventType => "UnknownEvent";

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
            {
                (this as IEntity<FakeChildState>).Apply(@event);
            }
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
    public interface SimpleUnitOfWork : Aggregates.UnitOfWork.IApplicationUnitOfWork { }
    public class FakeAppUnitOfWork : SimpleUnitOfWork
    {
        public Task Add<T>(Id id, T document) where T : class
        {
            throw new NotImplementedException();
        }

        public Task Delete<T>(Id id) where T : class
        {
            throw new NotImplementedException();
        }

        public Task<T> Get<T>(Id id) where T : class
        {
            throw new NotImplementedException();
        }

        public Task<IQueryResult<T>> Query<T>(IDefinition query) where T : class
        {
            throw new NotImplementedException();
        }

        public Task<T> TryGet<T>(Id id) where T : class
        {
            throw new NotImplementedException();
        }

        public Task Update<T>(Id id, T document) where T : class
        {
            throw new NotImplementedException();
        }
    }

    class FakeMetrics : Contracts.IMetrics
    {
        public Contracts.ITimer Begin(string name)
        {
            throw new NotImplementedException();
        }

        public void Decrement(string name, Contracts.Unit unit, long? value = null)
        {
            throw new NotImplementedException();
        }

        public void Increment(string name, Contracts.Unit unit, long? value = null)
        {
            throw new NotImplementedException();
        }

        public void Mark(string name, Contracts.Unit unit, long? value = null)
        {
            throw new NotImplementedException();
        }

        public void Update(string name, Contracts.Unit unit, long value)
        {
            throw new NotImplementedException();
        }
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
