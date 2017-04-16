using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    public abstract class Entity<TThis> : IEntity, IEventSourced, IHaveEntities<TThis>, INeedBuilder, INeedStream, INeedEventFactory, INeedRouteResolver where TThis : Entity<TThis>
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(TThis).Name);

        private IBuilder Builder => (this as INeedBuilder).Builder;
        
        private IMessageCreator EventFactory => (this as INeedEventFactory).EventFactory;

        private IRouteResolver Resolver => (this as INeedRouteResolver).Resolver;

        internal IEventStream Stream => (this as INeedStream).Stream;

        Id IEventSource.Id => Id;
        long IEventSource.Version => Version;
        IEventSource IEventSource.Parent => null;

        public Id Id => Stream.StreamId;
        public string Bucket => Stream.Bucket;
        public long Version => Stream.StreamVersion;
        public long CommitVersion => Stream.CommitVersion;

        public Task<long> EventCount => Stream.Size;
        public Task<long> OobCount => Stream.OobSize;


        IEventStream INeedStream.Stream { get; set; }
        IEventStream IEventSourced.Stream => (this as INeedStream).Stream;

        IMessageCreator INeedEventFactory.EventFactory { get; set; }

        IRouteResolver INeedRouteResolver.Resolver { get; set; }
        IBuilder INeedBuilder.Builder { get; set; }


        public IRepository<TThis, TEntity> For<TEntity>() where TEntity : Entity<TEntity, TThis>
        {
            // Get current UOW
            var uow = Builder.Build<IUnitOfWork>();
            return uow.For<TThis, TEntity>(this as TThis);
        }
        public IPocoRepository<TThis, T> Poco<T>() where T : class, new()
        {
            // Get current UOW
            var uow = Builder.Build<IUnitOfWork>();
            return uow.Poco<TThis, T>(this as TThis);
        }

        public Task<IEnumerable<IFullEvent>> Events(long? start = null, int? count = null)
        {
            return Stream.Events(start, count);
        }
        public Task<IEnumerable<IFullEvent>> OobEvents(long? start = null, int? count = null)
        {
            return Stream.OobEvents(start, count);
        }


        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        void IEventSourced.Hydrate(IEnumerable<IEvent> events)
        {
            Logger.Write(LogLevel.Debug, () => $"Hydrating {events.Count()} events to entity {GetType().FullName} stream [{Id}] bucket [{Bucket}]");
            foreach (var @event in events)
                RouteFor(@event);
        }
        void IEventSourced.Conflict(IEvent @event, IDictionary<string, string> metadata)
        {
            try
            {
                RouteForConflict(@event);
                RouteFor(@event);

                // Todo: Fill with user headers or something
                var headers = new Dictionary<string, string>();
                Stream.Add(@event, metadata);
            }
            catch (DiscardEventException) { }

        }

        void IEventSourced.Apply(IEvent @event, IDictionary<string, string> metadata)
        {
            Apply(@event, metadata);
        }
        void IEventSourced.Raise(IEvent @event, IDictionary<string, string> metadata)
        {
            Raise(@event, metadata);
        }

        /// <summary>
        /// Apply an event to the current object's eventstream
        /// </summary>
        protected void Apply<TEvent>(Action<TEvent> action, IDictionary<string, string> metadata = null) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Applying event {typeof(TEvent).FullName} to entity {GetType().FullName} stream [{Id}] bucket [{Bucket}]");
            var @event = EventFactory.CreateInstance(action);
            Apply(@event, metadata);
        }
        /// <summary>
        /// Publishes an event, but does not save to object's eventstream.  It will be stored under out of band event stream so as to not pollute object's
        /// </summary>
        protected void Raise<TEvent>(Action<TEvent> action, IDictionary<string, string> metadata = null) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Raising an OOB event {typeof(TEvent).FullName} on entity {GetType().FullName} stream [{Id}] bucket [{Bucket}]");
            var @event = EventFactory.CreateInstance(action);

            Raise(@event, metadata);
        }

        private void Apply(IEvent @event, IDictionary<string, string> metadata = null)
        {
            RouteFor(@event);
            
            // Todo: Fill with user headers or something
            var headers = new Dictionary<string, string>();
            Stream.Add(@event, metadata);
        }
        private void Raise(IEvent @event, IDictionary<string, string> metadata = null)
        {
            // Todo: Fill with user headers or something
            var headers = new Dictionary<string, string>();
            Stream.AddOutOfBand(@event, metadata);
        }

        
        internal void RouteFor(IEvent @event)
        {
            var route = Resolver.Resolve(this, @event.GetType());
            if (route == null)
            {
                Logger.Write(LogLevel.Debug, () => $"Failed to route event {@event.GetType().FullName} on type {typeof(TThis).FullName}");
                return;
            }

            route(this, @event);
        }
        internal void RouteForConflict(IEvent @event)
        {
            var route = Resolver.Conflict(this, @event.GetType());
            if (route == null)
                throw new NoRouteException($"Failed to route {@event.GetType().FullName} for conflict resolution on entity {typeof(TThis).FullName} stream id [{Id}] bucket [{Bucket}].  If you want to handle conflicts here, define a new method of signature `private void Conflict({@event.GetType().Name} e)`");

            route(this, @event);
        }
    }
}
