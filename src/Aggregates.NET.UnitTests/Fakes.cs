using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    public class FakeDomainEvent : IFullEvent
    {
        public class FakeEvent : IEvent { }

        public Guid? EventId { get; set; }

        public IEvent Event => new FakeEvent();

        public IEventDescriptor Descriptor => new EventDescriptor { StreamType = StreamTypes.Domain };
    }
    public class FakeOobEvent : IFullEvent
    {
        public class FakeEvent : IEvent { }

        public Guid? EventId { get; set; }

        public IEvent Event => new FakeEvent();

        public IEventDescriptor Descriptor => new EventDescriptor
        {
            StreamType = StreamTypes.OOB,
            Headers = new Dictionary<string, string>
            {
                [Defaults.OobHeaderKey] = "test",
            }
        };
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

        private void Conflict(FakeDomainEvent.FakeEvent e)
        {
            if (ThrowDiscard)
                throw new DiscardEventException();
            if (ThrowAbandon)
                throw new AbandonConflictException();

            Conflicts++;
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

    public class FakeEntity : Entity<FakeEntity, FakeState>
    {
        private FakeEntity() { }

        public void ApplyEvents<TEvent>(TEvent[] events) where TEvent : IEvent
        {
            foreach (var @event in events)
                (this as IEntity<FakeState>).Apply(@event);
        }
        public void RaiseEvents<TEvent>(TEvent[] events, string oobId, bool transient = true, int? daysToLive = null) where TEvent : IEvent
        {
            foreach (var @event in events)
                (this as IEntity<FakeState>).Raise(@event, oobId, transient, daysToLive);
        }
    }

    public class FakeChildEntity : Entity<FakeChildEntity, FakeState, FakeEntity>
    {
        private FakeChildEntity() { }
    }

    public class FakeEnumeration : Enumeration<FakeEnumeration, int>
    {
        public static FakeEnumeration One = new FakeEnumeration(1, "one");
        public static FakeEnumeration Two = new FakeEnumeration(2, "two");

        public FakeEnumeration(int value, string displayName) : base(value, displayName) { }
    }
}
