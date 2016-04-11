using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Generic;

namespace Aggregates
{
    public abstract class Aggregate<TId> : Entity<TId, TId>, IAggregate<TId>, INeedStream, INeedRepositoryFactory
    {
        internal new static readonly ILog Logger = LogManager.GetLogger(typeof(Aggregate<>));
    }

    public abstract class AggregateWithMemento<TId, TMemento> : Aggregate<TId>, ISnapshotting where TMemento : class, IMemento<TId>
    {
        void ISnapshotting.RestoreSnapshot(Object snapshot)
        {
            //Logger.DebugFormat("Restoring snapshot to {0} id {1} version {2}", this.GetType().FullName, this.Id, this.Version);
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
}