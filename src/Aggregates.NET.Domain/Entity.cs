using Aggregates.Contracts;
using Aggregates.Internal;
using Aggregates.Specifications;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{


    public abstract class Entity<TThis, TId, TParent, TParentId> : Base<TThis, TId>, IEntity<TId, TParent, TParentId> where TParent : Base<TParent, TParentId>  where TThis : Entity<TThis, TId, TParent, TParentId>
    {
        TParent IEntity<TId, TParent, TParentId>.Parent { get; set; }

        public TParent Parent { get { return (this as IEntity<TId, TParent, TParentId>).Parent; } }
    }

    public abstract class Entity<TThis, TId, TParent> : Entity<TThis, TId, TParent, TId> where TParent : Base<TParent, TId> where TThis : Entity<TThis, TId, TParent, TId> { }

    public abstract class EntityWithMemento<TThis, TId, TParent, TParentId, TMemento> : Entity<TThis, TId, TParent, TParentId>, ISnapshotting where TMemento : class, IMemento<TId> where TParent : Base<TParent, TParentId> where TThis : EntityWithMemento<TThis, TId, TParent, TParentId, TMemento>
    {
        void ISnapshotting.RestoreSnapshot(Object snapshot)
        {
            RestoreSnapshot(snapshot as TMemento);
        }

        Object ISnapshotting.TakeSnapshot()
        {
            return TakeSnapshot();
        }

        Boolean ISnapshotting.ShouldTakeSnapshot()
        {
            return ShouldTakeSnapshot();
        }

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected abstract Boolean ShouldTakeSnapshot();

    }
    public abstract class EntityWithMememto<TThis, TId, TParent, TMemento> : EntityWithMemento<TThis, TId, TParent, TId, TMemento> where TMemento : class, IMemento<TId> where TParent : Base<TParent, TId> where TThis : EntityWithMemento<TThis, TId, TParent, TId, TMemento> { }
}