using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus.Logging;

namespace Aggregates
{
    public abstract class Aggregate<TThis, TId> : Base<TThis, TId>, IAggregate<TId> where TThis : Aggregate<TThis, TId>
    {
        internal new static readonly ILog Logger = LogManager.GetLogger("Aggregate");
    }

    public abstract class AggregateWithMemento<TThis, TId, TMemento> : Aggregate<TThis, TId>, ISnapshotting where TMemento : class, IMemento<TId> where TThis : AggregateWithMemento<TThis, TId, TMemento>
    {
        int? ISnapshotting.LastSnapshot => Stream.LastSnapshot;

        void ISnapshotting.RestoreSnapshot(object snapshot)
        {
            RestoreSnapshot(snapshot as TMemento);
        }

        object ISnapshotting.TakeSnapshot()
        {
            return TakeSnapshot();
        }

        bool ISnapshotting.ShouldTakeSnapshot()
        {
            return ShouldTakeSnapshot();
        }

        public int? LastSnapshot => (this as ISnapshotting).LastSnapshot;

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected abstract bool ShouldTakeSnapshot();
        
    }
}