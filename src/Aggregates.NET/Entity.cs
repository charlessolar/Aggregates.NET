using Aggregates.Contracts;
using Aggregates.Specifications;
using NEventStore;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    // Implementation from http://stackoverflow.com/a/2326321/223547
    public abstract class Entity<TId> : IEntity<TId>, INeedStream, INeedEventFactory, INeedRouteResolver
    {
        private readonly TId _id;
        private IEventStream _eventStream { get { return (this as INeedStream).Stream; } }
        private IMessageCreator _eventFactory { get { return (this as INeedEventFactory).EventFactory; } }
        private IRouteResolver _resolver { get { return (this as INeedRouteResolver).Resolver; } }

        protected IList<Specification<Entity<TId>>> _specifications;

        public String BucketId { get { return ""; } }// _eventStream.BucketId; } }

        String IEventSource.StreamId { get { return this.StreamId; } }
        Int32 IEventSource.Version { get { return this.Version; } }

        public String StreamId { get { return _eventStream.StreamId; } }
        public Int32 Version { get { return _eventStream.StreamRevision; } }

        IEventStream INeedStream.Stream { get; set; }
        IMessageCreator INeedEventFactory.EventFactory { get; set; }
        IRouteResolver INeedRouteResolver.Resolver { get; set; }

        protected Entity(TId id)
        {
            if (object.Equals(id, default(TId)))
                throw new ArgumentException("The ID cannot be the default value.", "id");

            _specifications = new List<Specification<Entity<TId>>>();
            _id = id;
        }

        public TId Id
        {
            get { return _id; }
        }


        protected void AddSpecification(Specification<Entity<TId>> spec)
        {
            _specifications.Add(spec);
        }

        public Boolean IsSatisfied
        {
            get
            {
                if( _specifications.All(s => s.IsSatisfiedBy(this)))
                    return true;
                return false;
            }
        }


        
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        /// <inheritdoc />
        public bool Equals(Entity<TId> other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (ReferenceEquals(null, other)) return false;
            if (this.GetType() != other.GetType()) return false;

            return Id.Equals(other.Id);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return Equals(obj as Entity<TId>);
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

            Raise(@event);

            _eventStream.Add(new EventMessage
            {
                Body = @event,
                Headers = new Dictionary<string, object>
                {
                    { "Event", typeof(TEvent).FullName },
                    { "EventVersion", this.Version }
                    // Todo: Support user headers via method or attributes
                }
            });
        }
        private void Raise(object @event)
        {
            RouteFor(@event.GetType())(@event);
        }

        Action<Object> IEventRouter.RouteFor(Type eventType)
        {
            return RouteFor(eventType);
        }

        protected virtual Action<Object> RouteFor(Type eventType)
        {
            var route = _resolver.Resolve(this, eventType);

            if (route == null)
                throw new HandlerNotFoundException(String.Format("No handler for event {0}", eventType.Name));

            return e => route(e);
        }
    }

    public abstract class EntityWithMemento<TId, TMemento> : Entity<TId>, ISnapshotting where TMemento : class, IMemento
    {
        protected EntityWithMemento(TId id) : base(id) { }

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
        protected virtual Boolean ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion) { return false; }
    }
}
