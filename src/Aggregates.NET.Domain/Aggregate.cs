using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus.Logging;

namespace Aggregates
{
    public abstract class Aggregate<TThis> : Entity<TThis> where TThis : Aggregate<TThis>
    {
    }

    public abstract class AggregateWithMemento<TThis, TMemento> : Aggregate<TThis>, ISnapshotting where TMemento : class, IMemento where TThis : AggregateWithMemento<TThis, TMemento>
    {
        ISnapshot ISnapshotting.Snapshot => Stream.Snapshot;

        void ISnapshotting.RestoreSnapshot(IMemento snapshot)
        {
            RestoreSnapshot(snapshot as TMemento);
        }

        IMemento ISnapshotting.TakeSnapshot()
        {
            return TakeSnapshot();
        }

        bool ISnapshotting.ShouldTakeSnapshot()
        {
            return ShouldTakeSnapshot();
        }

        public ISnapshot Snapshot => (this as ISnapshotting).Snapshot;

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected abstract bool ShouldTakeSnapshot();
        
    }
}