using Aggregates.Contracts;
using Aggregates.Internal;

namespace Aggregates
{


    public abstract class Entity<TThis, TParent> : Entity<TThis>, IEntity<TParent> where TParent : Entity<TParent>  where TThis : Entity<TThis, TParent>
    {
        IEventSource IEventSource.Parent => Parent;
        
        public TParent Parent { get; internal set; }
    }
    

    public abstract class EntityWithMemento<TThis, TParent, TMemento> : Entity<TThis, TParent>, ISnapshotting where TMemento : class, IMemento where TParent : Entity<TParent> where TThis : EntityWithMemento<TThis, TParent, TMemento>
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