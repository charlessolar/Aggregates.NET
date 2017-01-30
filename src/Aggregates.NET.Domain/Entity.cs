using Aggregates.Contracts;
using Aggregates.Internal;

namespace Aggregates
{


    public abstract class Entity<TThis, TId, TParent, TParentId> : Base<TThis, TId>, IEntity<TId, TParent, TParentId> where TParent : Base<TParent, TParentId>  where TThis : Entity<TThis, TId, TParent, TParentId>
    {
        TParent IEntity<TId, TParent, TParentId>.Parent { get; set; }

        public TParent Parent => (this as IEntity<TId, TParent, TParentId>).Parent;
    }

    public abstract class Entity<TThis, TId, TParent> : Entity<TThis, TId, TParent, TId> where TParent : Base<TParent, TId> where TThis : Entity<TThis, TId, TParent, TId> { }

    public abstract class EntityWithMemento<TThis, TId, TParent, TParentId, TMemento> : Entity<TThis, TId, TParent, TParentId>, ISnapshotting where TMemento : class, IMemento<TId> where TParent : Base<TParent, TParentId> where TThis : EntityWithMemento<TThis, TId, TParent, TParentId, TMemento>
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
    public abstract class EntityWithMememto<TThis, TId, TParent, TMemento> : EntityWithMemento<TThis, TId, TParent, TId, TMemento> where TMemento : class, IMemento<TId> where TParent : Base<TParent, TId> where TThis : EntityWithMemento<TThis, TId, TParent, TId, TMemento> { }
}