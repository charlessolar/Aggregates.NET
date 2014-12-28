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
        public TId Id
        {
            get
            {
                // Dont use unsupported Ids kids
                var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(TId));
                if (converter != null && converter.IsValid(this.StreamId))
                    return (TId)converter.ConvertFromString(this.StreamId);
                else
                    return (TId)Activator.CreateInstance(typeof(TId));
            }
        }
        public String BucketId
        {
            get
            {
                // IEventStream does not have a bucketid property yet
                return "";
            }
        }

        String IEventSource.StreamId { get { return this.StreamId; } }
        Int32 IEventSource.Version { get { return this.Version; } }

        public String StreamId { get { return _eventStream.StreamId; } }
        public Int32 Version { get { return _eventStream.StreamRevision; } }

        private readonly IContainer _container;
        private readonly IEventStream _eventStream;
        private readonly IMessageCreator _eventFactory;
        protected readonly IEventRouter _router;


        protected Aggregate(IContainer container, IEventRouter router = null)
        {
            _container = container;
            _router = router ?? _container.Build(typeof(IEventRouter)) as IEventRouter;
            _eventFactory = _container.Build(typeof(IMessageCreator)) as IMessageCreator;
            _eventStream = _container.Build(typeof(IEventStream)) as IEventStream;

            _router.Register(this);
        }


        void IEventSource.Hydrate(IEnumerable<object> events)
        {
            foreach (var @event in events)
            {
                Raise(@event);
            }
        }
        void IEventSource.Apply<TEvent>(Action<TEvent> action)
        {
            Apply(action);
        }

        protected void Apply<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);
            Apply(@event);
        }
        protected void Apply<TEvent>(TEvent @event)
        {
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
        protected AggregateWithMemento(IContainer container, IEventRouter router = null)
            : base(container, router)
        {
        }

        void ISnapshottingEventSource.RestoreSnapshot(ISnapshot snapshot)
        {
            var memento = (TMemento)snapshot.Payload;

            RestoreSnapshot(memento);
        }

        ISnapshot ISnapshottingEventSource.TakeSnapshot()
        {
            var memento = TakeSnapshot();
            memento.Id = this.Id;
            return new Snapshot(this.BucketId, this.StreamId, this.Version, memento);
        }

        Boolean ISnapshottingEventSource.ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion)
        {
            return ShouldTakeSnapshot(CurrentVersion, CommitVersion);
        }

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected virtual Boolean ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion) { return false; }
    }
}