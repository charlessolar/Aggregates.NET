using Aggregates.Contracts;
using Aggregates.Internal;

namespace Aggregates
{


    public abstract class Entity<TThis, TParent> : Base<TThis>, IEntity<TParent> where TParent : Base<TParent>  where TThis : Entity<TThis, TParent>
    {
        IEventSource IEventSource.Parent => Parent;

        TParent IEntity<TParent>.Parent { get; set; }

        public TParent Parent => (this as IEntity<TParent>).Parent;
    }
    

    public abstract class EntityWithMemento<TThis, TParent, TMemento> : Entity<TThis, TParent>, ISnapshotting where TMemento : class, IMemento where TParent : Base<TParent> where TThis : EntityWithMemento<TThis, TParent, TMemento>
    {
        ISnapshot ISnapshotting.Snapshot => Stream.Snapshot;

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

        public ISnapshot Snapshot => (this as ISnapshotting).Snapshot;

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected abstract bool ShouldTakeSnapshot();

    }
    
}