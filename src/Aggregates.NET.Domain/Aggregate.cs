using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Generic;

namespace Aggregates
{
    public abstract class Aggregate<TThis, TId> : Base<TThis, TId>, IAggregate<TId>, INeedStream, INeedRepositoryFactory where TThis : Aggregate<TThis, TId>
    {
        internal new static readonly ILog Logger = LogManager.GetLogger(typeof(Aggregate<,>));
    }

    public abstract class AggregateWithMemento<TThis, TId, TMemento> : Aggregate<TThis, TId>, ISnapshotting where TMemento : class, IMemento<TId> where TThis : AggregateWithMemento<TThis, TId, TMemento>
    {
        Int32? ISnapshotting.LastSnapshot
        {
            get
            {
                return this.Stream.LastSnapshot;
            }
        }
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

        public Int32? LastSnapshot
        {
            get
            {
                return (this as ISnapshotting).LastSnapshot;
            }
        }

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected abstract Boolean ShouldTakeSnapshot();
        
    }
}