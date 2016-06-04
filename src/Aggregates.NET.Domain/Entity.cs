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
    public abstract class Entity<TId, TAggregateId> : IEntity<TId, TAggregateId>, IHaveEntities<TId>, INeedBuilder, INeedStream, INeedEventFactory, INeedRouteResolver, INeedRepositoryFactory, INeedProcessor
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(Entity<,>));
        private IDictionary<Type, IEntityRepository> _repositories = new Dictionary<Type, IEntityRepository>();

        private IBuilder _builder { get { return (this as INeedBuilder).Builder; } }
        private IRepositoryFactory _repoFactory { get { return (this as INeedRepositoryFactory).RepositoryFactory; } }

        private IProcessor _processor { get { return (this as INeedProcessor).Processor; } }
        private IMessageCreator _eventFactory { get { return (this as INeedEventFactory).EventFactory; } }

        private IRouteResolver _resolver { get { return (this as INeedRouteResolver).Resolver; } }

        public TId Id { get { return (this as IEventSource<TId>).Id; } }

        public TAggregateId AggregateId { get { return (this as IEntity<TId, TAggregateId>).AggregateId; } }

        String IEventSource.Bucket { get { return this.Bucket; } }
        String IEventSource.StreamId { get { return this.StreamId; } }

        Int32 IEventSource.Version { get { return this.Version; } }

        public IEventStream Stream { get { return (this as INeedStream).Stream; } }
        public String Bucket { get { return Stream.Bucket; } }
        public String StreamId { get { return Stream.StreamId; } }

        public Int32 Version { get { return Stream.StreamVersion; } }

        public Int32 CommitVersion { get { return Stream.CommitVersion; } }

        IEventStream INeedStream.Stream { get; set; }
        IRepositoryFactory INeedRepositoryFactory.RepositoryFactory { get; set; }

        IProcessor INeedProcessor.Processor { get; set; }
        IMessageCreator INeedEventFactory.EventFactory { get; set; }

        IRouteResolver INeedRouteResolver.Resolver { get; set; }
        IBuilder INeedBuilder.Builder { get; set; }

        TId IEventSource<TId>.Id { get; set; }

        TAggregateId IEntity<TId, TAggregateId>.AggregateId { get; set; }


        public IEntityRepository<TId, TEntity> For<TEntity>() where TEntity : class, IEntity
        {
            // Get current UOW
            var uow = _builder.Build<IUnitOfWork>();
            return uow.For<TId, TEntity>(this);
        }
        public Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(TQuery query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var processor = _builder.Build<IProcessor>();
            return processor.Process<TQuery, TResponse>(_builder, query);
        }
        public Task<IEnumerable<TResponse>> Query<TQuery, TResponse>(Action<TQuery> query) where TResponse : IQueryResponse where TQuery : IQuery<TResponse>
        {
            var result = (TQuery)FormatterServices.GetUninitializedObject(typeof(TQuery));
            query.Invoke(result);
            return Query<TQuery, TResponse>(result);
        }

        public Task<TResponse> Compute<TComputed, TResponse>(TComputed computed) where TComputed : IComputed<TResponse>
        {
            var processor = _builder.Build<IProcessor>();
            return processor.Compute<TComputed, TResponse>(_builder, computed);
        }
        public Task<TResponse> Compute<TComputed, TResponse>(Action<TComputed> computed) where TComputed : IComputed<TResponse>
        {
            var result = (TComputed)FormatterServices.GetUninitializedObject(typeof(TComputed));
            computed.Invoke(result);
            return Compute<TComputed, TResponse>(result);
        }


        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        void IEventSource.Hydrate(IEnumerable<object> events)
        {
            foreach (var @event in events)
                Route(@event);
        }

        void IEventSource.Apply<TEvent>(Action<TEvent> action)
        {
            Apply(action);
        }

        /// <summary>
        /// Apply an event to the current object's eventstream
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="action"></param>
        protected void Apply<TEvent>(Action<TEvent> action) where TEvent : IEvent
        {
            var @event = _eventFactory.CreateInstance(action);

            Route(@event);

            // Todo: Fill with user headers or something
            var headers = new Dictionary<String, String>();
            Stream.Add(@event, headers);
        }
        /// <summary>
        /// Issues an event, but do not save to object's eventstream.  It will be stored under a general events system stream
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="action"></param>
        protected void Raise<TEvent>(Action<TEvent> action) where TEvent : IEvent
        {
            var @event = _eventFactory.CreateInstance(action);

            var headers = new Dictionary<String, String>();
            headers["Version"] = this.Version.ToString();
            headers["Bucket"] = this.Bucket;
            headers["StreamId"] = this.StreamId;


        }

        private void Route(object @event)
        {
            if (@event == null) return;

            RouteFor(@event.GetType(), @event);
        }

        void IEventRouter.RouteFor(Type @eventType, object @event)
        {
            RouteFor(eventType, @event);
        }

        internal void RouteFor(Type eventType, object @event)
        {
            var route = _resolver.Resolve(this, eventType);
            if (route == null) return;

            route(this, @event);
        }
    }

    public abstract class EntityWithMemento<TId, TAggregateId, TMemento> : Entity<TId, TAggregateId>, ISnapshotting where TMemento : class, IMemento<TId>
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

    public abstract class Entity<TId> : Entity<TId, TId> { }

    public abstract class EntityWithMemento<TId, TMemento> : EntityWithMemento<TId, TId, TMemento> where TMemento : class, IMemento<TId> { }
}