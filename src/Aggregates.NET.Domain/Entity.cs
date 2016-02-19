using Aggregates.Contracts;
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
    public abstract class Entity<TId, TAggregateId> : IEntity<TId, TAggregateId>, IHaveEntities<TId>, INeedBuilder, INeedStream, INeedEventFactory, INeedRouteResolver, INeedMutator, INeedRepositoryFactory, INeedProcessor
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(Entity<,>));
        private IDictionary<Type, IEntityRepository> _repositories = new Dictionary<Type, IEntityRepository>();

        private IBuilder _builder { get { return (this as INeedBuilder).Builder; } }
        private IEventStream _eventStream { get { return (this as INeedStream).Stream; } }
        private IRepositoryFactory _repoFactory { get { return (this as INeedRepositoryFactory).RepositoryFactory; } }

        private IProcessor _processor { get { return (this as INeedProcessor).Processor; } }
        private IMessageCreator _eventFactory { get { return (this as INeedEventFactory).EventFactory; } }

        private IRouteResolver _resolver { get { return (this as INeedRouteResolver).Resolver; } }
        private IEventMutator _mutator { get { return (this as INeedMutator).Mutator; } }

        public TId Id { get { return (this as IEventSource<TId>).Id; } }

        public TAggregateId AggregateId { get { return (this as IEntity<TId, TAggregateId>).AggregateId; } }

        String IEventSource.Bucket { get { return this.Bucket; } }
        String IEventSource.StreamId { get { return this.StreamId; } }

        Int32 IEventSource.Version { get { return this.Version; } }

        public String Bucket { get { return _eventStream.Bucket; } }
        public String StreamId { get { return _eventStream.StreamId; } }

        public Int32 Version { get { return _eventStream.StreamVersion; } }

        public Int32 CommitVersion { get { return _eventStream.CommitVersion; } }

        IEventStream INeedStream.Stream { get; set; }
        IRepositoryFactory INeedRepositoryFactory.RepositoryFactory { get; set; }

        IProcessor INeedProcessor.Processor { get; set; }
        IMessageCreator INeedEventFactory.EventFactory { get; set; }

        IRouteResolver INeedRouteResolver.Resolver { get; set; }
        IEventMutator INeedMutator.Mutator { get; set; }
        IBuilder INeedBuilder.Builder { get; set; }

        TId IEventSource<TId>.Id { get; set; }

        TAggregateId IEntity<TId, TAggregateId>.AggregateId { get; set; }

        
        public IEntityRepository<TId, TEntity> For<TEntity>() where TEntity : class, IEntity
        {
            Logger.DebugFormat("Retreiving entity repository for type {0}", typeof(TEntity));
            var type = typeof(TEntity);

            IEntityRepository repository;
            if (_repositories.TryGetValue(type, out repository))
                return (IEntityRepository<TId, TEntity>)repository;

            return (IEntityRepository<TId, TEntity>)(_repositories[type] = (IEntityRepository)_repoFactory.ForEntity<TId, TEntity>(Id, _eventStream, _builder));
        }
        public IEnumerable<TResponse> Query<TQuery, TResponse>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            return _processor.Process<TResponse, TQuery>(query);
        }
        public IEnumerable<TResponse> Query<TQuery, TResponse>(Action<TQuery> query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var result = (TQuery)FormatterServices.GetUninitializedObject(typeof(TQuery));
            query.Invoke(result);
            return _processor.Process<TResponse, TQuery>(result);
        }

        public TResponse Compute<TComputed, TResponse>(TComputed computed) where TComputed : IComputed<TResponse>
        {
            return _processor.Compute<TResponse, TComputed>(computed);
        }
        public TResponse Compute<TComputed, TResponse>(Action<TComputed> computed) where TComputed : IComputed<TResponse>
        {
            var result = (TComputed)FormatterServices.GetUninitializedObject(typeof(TComputed));
            computed.Invoke(result);
            return Compute<TComputed, TResponse>(result);
        }
        

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        void IEventSource<TId>.Hydrate(IEnumerable<object> events)
        {
            foreach (var @event in events)
                Raise(@event);
        }

        void IEventSource<TId>.Apply<TEvent>(Action<TEvent> action)
        {
            Apply(action);
        }

        internal virtual void Apply<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);

            if (this._mutator != null)
                this._mutator.MutateEvent(@event);

            Raise(@event);

            // Todo: Fill with user headers or something
            var headers = new Dictionary<String, Object>();
            _eventStream.Add(@event, headers);
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

        internal virtual void RouteFor(Type eventType, object @event)
        {
            var route = _resolver.Resolve(this, eventType);
            if (route == null) return;

            route(@event);
        }
    }

    public abstract class EntityWithMemento<TId, TAggregateId, TMemento> : Entity<TId, TAggregateId>, ISnapshotting where TMemento : class, IMemento<TId>
    {
        private IEventStream _eventStream { get { return (this as INeedStream).Stream; } }

        void ISnapshotting.RestoreSnapshot(Object snapshot)
        {
            Logger.DebugFormat("Restoring snapshot to {0} id {1} version {2}", this.GetType().FullName, this.Id, this.Version);
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

        internal override void Apply<TEvent>(Action<TEvent> action)
        {
            base.Apply(action);

            if (this.ShouldTakeSnapshot())
            {
                Logger.DebugFormat("Taking snapshot of {0} id {1} version {2}", this.GetType().FullName, this.Id, this.Version);
                _eventStream.AddSnapshot((this as ISnapshotting).TakeSnapshot(), new Dictionary<string, object> { });
            }
        }
    }

    public abstract class Entity<TId> : Entity<TId, TId> { }

    public abstract class EntityWithMemento<TId, TMemento> : EntityWithMemento<TId, TId, TMemento> where TMemento : class, IMemento<TId> { }
}