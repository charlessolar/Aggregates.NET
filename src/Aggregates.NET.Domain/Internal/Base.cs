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
    public abstract class Base<TThis, TId> : IBase<TId>, IHaveEntities<TThis, TId>, INeedBuilder, INeedStream, INeedEventFactory, INeedRouteResolver where TThis : Base<TThis, TId>
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(TThis).Name);

        private IBuilder Builder => (this as INeedBuilder).Builder;
        
        private IMessageCreator EventFactory => (this as INeedEventFactory).EventFactory;

        private IRouteResolver Resolver => (this as INeedRouteResolver).Resolver;

        public TId Id => (this as IEventSource<TId>).Id;

        string IEventSource.Bucket => Bucket;
        string IEventSource.StreamId => StreamId;

        int IEventSource.Version => Version;

        public IEventStream Stream => (this as INeedStream).Stream;
        public string Bucket => Stream.Bucket;
        public string StreamId => Stream.StreamId;

        public int Version => Stream.StreamVersion;

        public int CommitVersion => Stream.CommitVersion;

        IEventStream INeedStream.Stream { get; set; }
        
        IMessageCreator INeedEventFactory.EventFactory { get; set; }

        IRouteResolver INeedRouteResolver.Resolver { get; set; }
        IBuilder INeedBuilder.Builder { get; set; }

        TId IEventSource<TId>.Id { get; set; }

        public IEntityRepository<TThis, TId, TEntity> For<TEntity>() where TEntity : class, IEntity
        {
            // Get current UOW
            var uow = Builder.Build<IUnitOfWork>();
            return uow.For<TThis, TId, TEntity>(this as TThis);
        }
        public IPocoRepository<TThis, TId, T> Poco<T>() where T : class, new()
        {
            // Get current UOW
            var uow = Builder.Build<IUnitOfWork>();
            return uow.Poco<TThis, TId, T>(this as TThis);
        }


        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        void IEventSource.Hydrate(IEnumerable<IEvent> events)
        {
            Logger.Write(LogLevel.Debug, () => $"Hydrating {events.Count()} events to entity {GetType().FullName} stream [{StreamId}]");
            foreach (var @event in events)
                RouteFor(@event);
        }
        void IEventSource.Conflict(IEvent @event)
        {
            try
            {
                RouteForConflict(@event);
                RouteFor(@event);

                // Todo: Fill with user headers or something
                var headers = new Dictionary<string, string>();
                Stream.Add(@event, headers);
            }
            catch (DiscardEventException) { }

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
            Logger.Write(LogLevel.Debug, () => $"Applying event {typeof(TEvent).FullName} to entity {GetType().FullName} stream [{StreamId}]");
            var @event = EventFactory.CreateInstance(action);
            Apply(@event);
        }
        /// <summary>
        /// Publishes an event, but does not save to object's eventstream.  It will be stored under out of band event stream so as to not pollute object's
        /// </summary>
        /// <typeparam name="TEvent"></typeparam>
        /// <param name="action"></param>
        protected void Raise<TEvent>(Action<TEvent> action) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Raising an OOB event {typeof(TEvent).FullName} on entity {GetType().FullName} stream [{StreamId}]");
            var @event = EventFactory.CreateInstance(action);

            Raise(@event);
        }

        private void Apply(IEvent @event)
        {
            RouteFor(@event);

            // Todo: Fill with user headers or something
            var headers = new Dictionary<string, string>();
            Stream.Add(@event, headers);
        }
        private void Raise(IEvent @event)
        {
            var headers = new Dictionary<string, string>
            {
                ["Bucket"] = Bucket,
                ["EntityType"] = typeof(TThis).FullName,
                ["StreamId"] = StreamId
            };

            Stream.AddOutOfBand(@event, headers);
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
                throw new NoRouteException($"Failed to route {@event.GetType().FullName} for conflict resolution on entity {typeof(TThis).FullName} stream id {StreamId}.  If you want to handle conflicts here, define a new method of signature `private void Conflict({@event.GetType().Name} e)`");

            route(this, @event);
        }
    }
}
