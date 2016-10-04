using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
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

namespace Aggregates.Internal
{
    public abstract class Base<TThis, TId> : IBase<TId>, IHaveEntities<TThis, TId>, INeedBuilder, INeedStream, INeedEventFactory, INeedRouteResolver, INeedRepositoryFactory, INeedProcessor where TThis : Base<TThis, TId>
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(Base<,>));
        private IDictionary<Type, IEntityRepository> _repositories = new Dictionary<Type, IEntityRepository>();

        private IBuilder _builder { get { return (this as INeedBuilder).Builder; } }
        private IRepositoryFactory _repoFactory { get { return (this as INeedRepositoryFactory).RepositoryFactory; } }

        private IProcessor _processor { get { return (this as INeedProcessor).Processor; } }
        private IMessageCreator _eventFactory { get { return (this as INeedEventFactory).EventFactory; } }

        private IRouteResolver _resolver { get { return (this as INeedRouteResolver).Resolver; } }

        public TId Id { get { return (this as IEventSource<TId>).Id; } }

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

        public IEntityRepository<TThis, TId, TEntity> For<TEntity>() where TEntity : class, IEntity
        {
            // Get current UOW
            var uow = _builder.Build<IUnitOfWork>();
            return uow.For<TThis, TId, TEntity>(this as TThis);
        }
        public IPocoRepository<TThis, TId, T> Poco<T>() where T : class, new()
        {
            // Get current UOW
            var uow = _builder.Build<IUnitOfWork>();
            return uow.Poco<TThis, TId, T>(this as TThis);
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

        void IEventSource.Hydrate(IEnumerable<IEvent> events)
        {
            Logger.Write(LogLevel.Debug, () => $"Hydrating {events.Count()} events to entity {this.GetType().FullName} stream {this.StreamId}");
            foreach (var @event in events)
                RouteFor(@event);
        }
        void IEventSource.Conflict(IEnumerable<IEvent> events)
        {
            Logger.Write(LogLevel.Debug, () => $"Attempting to merge {events.Count()} events to entity {this.GetType().FullName} stream {this.StreamId}");
            foreach (var @event in events)
            {
                try
                {
                    RouteForConflict(@event);
                    RouteFor(@event);

                    // Todo: Fill with user headers or something
                    var headers = new Dictionary<String, String>();
                    Stream.Add(@event, headers);
                }
                catch (DiscardEventException) { }
            }
        }

        void IEventSource.Apply(IEvent @event)
        {
            Apply(@event);
        }
        void IEventSource.Raise(IEvent @event)
        {
            Raise(@event);
        }

        /// <summary>
        /// Apply an event to the current object's eventstream
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="action"></param>
        protected void Apply<TEvent>(Action<TEvent> action) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Applying event {typeof(TEvent).FullName} to entity {this.GetType().FullName} stream {this.StreamId}");
            var @event = _eventFactory.CreateInstance(action);
            Apply(@event);
        }
        /// <summary>
        /// Publishes an event, but does not save to object's eventstream.  It will be stored under out of band event stream so as to not pollute object's
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="action"></param>
        protected void Raise<TEvent>(Action<TEvent> action) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Raising an OOB event {typeof(TEvent).FullName} on entity {this.GetType().FullName} stream {this.StreamId}");
            var @event = _eventFactory.CreateInstance(action);

            Raise(@event);
        }
        
        private void Apply(IEvent @event)
        {
            RouteFor(@event);

            // Todo: Fill with user headers or something
            var headers = new Dictionary<String, String>();
            Stream.Add(@event, headers);
        }
        private void Raise(IEvent @event)
        {
            var headers = new Dictionary<String, String>();
            headers["Bucket"] = this.Bucket;
            headers["StreamId"] = this.StreamId;

            Stream.AddOutOfBand(@event, headers);
        }

        internal void RouteFor<TEvent>(TEvent @event) where TEvent : IEvent
        {
            var route = _resolver.Resolve(this, @event.GetType());
            if (route == null) return;

            route(this, @event);
        }
        internal void RouteForConflict<TEvent>(TEvent @event) where TEvent : IEvent
        {
            var route = _resolver.Conflict(this, @event.GetType());
            if (route == null)
                throw new NoRouteException($"Failed to route {@event.GetType().FullName} for conflict resolution on entity {typeof(TThis).FullName}.  If you want to handle conflicts here, define a new method of signature `private void Conflict({@event.GetType().Name} e)`");

            route(this, @event);
        }
    }
}
