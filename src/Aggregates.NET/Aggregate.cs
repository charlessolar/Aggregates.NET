using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Generic;

namespace Aggregates
{

    public abstract class Aggregate<TId> : IEventSource<TId>
    {
        public TId Id { get; protected set; }

        String IEventSourceBase.StreamId { get { return this.Id.ToString(); } }
        String IEventSourceBase.BucketId { get { return this.BucketId; } }
        Int32 IEventSourceBase.Version { get { return this.Version; } }

        public String BucketId { get; protected set; }
        public Int32 Version { get; protected set; }

        public virtual IContainer Container { get; set; }

        private readonly IEventStream _eventStream;
        private readonly IMessageCreator _eventFactory;
        protected readonly IEventRouter _router;


        protected Aggregate()
        {
            _router = Container.Build(typeof(IEventRouter)) as IEventRouter;
            _eventFactory = Container.Build(typeof(IMessageCreator)) as IMessageCreator;
            _eventStream = Container.Build(typeof(IEventStream)) as IEventStream;
            _router.Register(this);
        }
        protected Aggregate(IContainer container, IEventRouter router = null)
        {
            this.Container = container;
            _router = router ?? Container.Build(typeof(IEventRouter)) as IEventRouter;
            _eventFactory = Container.Build(typeof(IMessageCreator)) as IMessageCreator;
            _eventStream = Container.Build(typeof(IEventStream)) as IEventStream;

            _router.Register(this);
        }

        
        void IEventSourceBase.Hydrate(IEnumerable<object> events)
        {
            foreach (var @event in events)
            {
                Raise(@event);
                Version++;
            }
        }

        void IEventSourceBase.Apply<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);

            Raise(@event);

            _eventStream.Add(new EventMessage
            {
                Body = @event,
                Headers = new Dictionary<string, object>
                {
                    { "EventVersion", this.Version }
                    // Todo: Support user headers via method or attributes
                }
            });
        }


        private void Raise(object @event)
        {
            _router.Get(@event.GetType())(@event);
        }

    }

    public abstract class AggregateWithMemento<TId, TMemento> : Aggregate<TId>, ISnapshottingEventSource<TId> where TMemento : class, IMemento<TId>
    {
        void ISnapshottingEventSourceBase.RestoreSnapshot(ISnapshot snapshot)
        {
            Version = snapshot.StreamRevision;
            BucketId = snapshot.BucketId;

            var memento = (TMemento)snapshot.Payload;

            RestoreSnapshot(memento);
        }

        ISnapshot ISnapshottingEventSourceBase.TakeSnapshot()
        {
            var memento = TakeSnapshot();
            return new Snapshot(this.BucketId, this.Id.ToString(), this.Version, memento);
        }

        Boolean ISnapshottingEventSourceBase.ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion)
        {
            return ShouldTakeSnapshot(CurrentVersion, CommitVersion);
        }

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected virtual Boolean ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion) { return false; }
    }
}