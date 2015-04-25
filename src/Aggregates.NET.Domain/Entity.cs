using Aggregates.Contracts;
using Aggregates.Specifications;
using EventStore.ClientAPI;
using NServiceBus;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public abstract class Entity<TId, TAggregateId> : IEntity<TId, TAggregateId>, INeedStream, INeedEventFactory, INeedRouteResolver
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Entity<,>));

        private AllEventsSlice _eventStream { get { return (this as INeedStream).Stream; } }

        private IMessageCreator _eventFactory { get { return (this as INeedEventFactory).EventFactory; } }

        private IRouteResolver _resolver { get { return (this as INeedRouteResolver).Resolver; } }

        public TId Id { get { return (this as IEventSource<TId>).Id; } }

        public TAggregateId AggregateId { get { return (this as IEntity<TId, TAggregateId>).AggregateId; } }

        public String BucketId { get { return (this as IEventSource<TId>).BucketId; } }

        String IEventSource.StreamId { get { return this.StreamId; } }

        Int32 IEventSource.Version { get { return this.Version; } }

        public String StreamId { get { return _eventStream.StreamId; } }

        public Int32 Version { get { return _eventStream.StreamRevision; } }

        AllEventsSlice INeedStream.Stream { get; set; }

        IMessageCreator INeedEventFactory.EventFactory { get; set; }

        IRouteResolver INeedRouteResolver.Resolver { get; set; }

        TId IEventSource<TId>.Id { get; set; }

        TAggregateId IEntity<TId, TAggregateId>.AggregateId { get; set; }

        String IEventSource<TId>.BucketId { get; set; }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        void IEventSource.Hydrate(IEnumerable<object> events)
        {
            foreach (var @event in events)
                Raise(@event);
        }

        void IEventSource.Apply<TEvent>(Action<TEvent> action)
        {
            Apply(action);
        }

        protected void Apply<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);

            Raise(@event);

            _eventStream.Add(new EventData
            {
                Body = @event,
                Headers = new Dictionary<string, object>
                {
                    { "Id", this.Id },
                    { "Entity", this.GetType().FullName },
                    { "Event", typeof(TEvent).FullName },
                    { "EventVersion", this.Version }
                    // Todo: Support user headers via method or attributes
                }
            });
        }

        private void Raise(object @event)
        {
            if (@event == null) return;

            RouteFor(@event.GetType(), @event);
        }

        void IEventRouter.RouteFor(Type @eventType, object @event)
        {
            RouteFor(eventType, @event);
        }

        protected virtual void RouteFor(Type eventType, object @event)
        {
            var route = _resolver.Resolve(this, eventType);
            if (route == null) return;

            route(@event);
        }
    }

    public abstract class EntityWithMemento<TId, TAggregateId, TMemento> : Entity<TId, TAggregateId>, ISnapshotting where TMemento : class, IMemento
    {
        void ISnapshotting.RestoreSnapshot(ISnapshot snapshot)
        {
            var memento = (TMemento)snapshot.Payload;

            RestoreSnapshot(memento);
        }

        ISnapshot ISnapshotting.TakeSnapshot()
        {
            var memento = TakeSnapshot();
            return new Snapshot(this.BucketId, this.StreamId, this.Version, memento);
        }

        Boolean ISnapshotting.ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion)
        {
            return ShouldTakeSnapshot(CurrentVersion, CommitVersion);
        }

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected virtual Boolean ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion)
        {
            return false;
        }
    }

    public abstract class Entity<TId> : Entity<TId, TId> { }

    public abstract class EntityWithMemento<TId, TMemento> : EntityWithMemento<TId, TId, TMemento> where TMemento : class, IMemento { }
}