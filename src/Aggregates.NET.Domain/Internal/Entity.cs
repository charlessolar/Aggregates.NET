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
    public abstract class Entity<TThis, TParent> : State<TThis, TParent>, IEventSourced, IHaveEntities<TThis>, INeedEventFactory where TParent : Entity<TParent> where TThis : Entity<TThis, TParent>
    {
        private IMessageCreator EventFactory => (this as INeedEventFactory).EventFactory;

        IMessageCreator INeedEventFactory.EventFactory { get; set; }


        public Task<long> GetSize(string oob)
        {
            var store = Builder.Build<IStoreStreams>();
            return store.GetSize<TThis>(Stream, oob);
        }

        public Task<IEnumerable<IFullEvent>> GetEvents(long start, int count, string oob = null)
        {
            var store = Builder.Build<IStoreStreams>();
            return store.GetEvents<TThis>(Stream, start, count, oob);
        }

        public Task<IEnumerable<IFullEvent>> GetEventsBackwards(long start, int count, string oob = null)
        {
            var store = Builder.Build<IStoreStreams>();
            return store.GetEventsBackwards<TThis>(Stream, start, count, oob);
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
        void IEventSourced.Raise(IEvent @event, string id, IDictionary<string, string> metadata)
        {
            Raise(@event, id, metadata);
        }

        protected void DefineOob(string id, bool transient = false, int? daysToLive = null)
        {
            Stream.DefineOob(id, transient, daysToLive);
        }

        /// <summary>
        /// Apply an event to the current object's eventstream
        /// </summary>
        protected void Apply<TEvent>(Action<TEvent> action, IDictionary<string, string> metadata = null) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Applying event {typeof(TEvent).FullName} to entity {GetType().FullName} stream [{Id}] bucket [{Bucket}]");
            var @event = EventFactory.CreateInstance(action);

            if (@event == null)
                throw new ArgumentException($"Failed to build event type {typeof(TEvent).FullName}");
            Apply(@event, metadata);
        }
        /// <summary>
        /// Publishes an event, but does not save to object's eventstream.  It will be stored under out of band event stream so as to not pollute entity's
        /// </summary>
        protected void Raise<TEvent>(Action<TEvent> action, string id, IDictionary<string, string> metadata = null) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Raising an OOB event {typeof(TEvent).FullName} on entity {GetType().FullName} stream [{Id}] bucket [{Bucket}]");
            var @event = EventFactory.CreateInstance(action);

            if (@event == null)
                throw new ArgumentException($"Failed to build event type {typeof(TEvent).FullName}");
            Raise(@event, id, metadata);
        }

        private void Apply(IEvent @event, IDictionary<string, string> metadata = null)
        {
            RouteFor(@event);

            // Todo: Fill with user headers or something
            Stream.Add(@event, metadata);
        }
        private void Raise(IEvent @event, string id, IDictionary<string, string> metadata = null)
        {
            // Todo: Fill metadata with user headers or something
            Stream.AddOob(@event, id, metadata);
        }


        internal void RouteForConflict(IEvent @event)
        {
            var route = Resolver.Conflict(this, @event.GetType());
            if (route == null)
                throw new NoRouteException($"Failed to route {@event.GetType().FullName} for conflict resolution on entity {typeof(TThis).FullName} stream id [{Id}] bucket [{Bucket}].  If you want to handle conflicts here, define a new method of signature `private void Conflict({@event.GetType().Name} e)`");

            route(this, @event);
        }
    }
    public abstract class Entity<TThis> : State<TThis>, IEventSourced, IHaveEntities<TThis>, INeedEventFactory where TThis : Entity<TThis>
    {
        
        private IMessageCreator EventFactory => (this as INeedEventFactory).EventFactory;

        IMessageCreator INeedEventFactory.EventFactory { get; set; }
        

        public Task<long> GetSize(string oob)
        {
            var store = Builder.Build<IStoreStreams>();
            return store.GetSize<TThis>(Stream, oob);
        }

        public Task<IEnumerable<IFullEvent>> GetEvents(long start, int count, string oob = null)
        {
            var store = Builder.Build<IStoreStreams>();
            return store.GetEvents<TThis>(Stream, start, count, oob);
        }

        public Task<IEnumerable<IFullEvent>> GetEventsBackwards(long start, int count, string oob = null)
        {
            var store = Builder.Build<IStoreStreams>();
            return store.GetEventsBackwards<TThis>(Stream, start, count, oob);
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
        void IEventSourced.Raise(IEvent @event, string id, IDictionary<string, string> metadata)
        {
            Raise(@event, id, metadata);
        }

        protected void DefineOob(string id, bool transient = false, int? daysToLive = null)
        {
            Stream.DefineOob(id, transient, daysToLive);
        }

        /// <summary>
        /// Apply an event to the current object's eventstream
        /// </summary>
        protected void Apply<TEvent>(Action<TEvent> action, IDictionary<string, string> metadata = null) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Applying event {typeof(TEvent).FullName} to entity {GetType().FullName} stream [{Id}] bucket [{Bucket}]");
            var @event = EventFactory.CreateInstance(action);

            if (@event == null)
                throw new ArgumentException($"Failed to build event type {typeof(TEvent).FullName}");
            Apply(@event, metadata);
        }
        /// <summary>
        /// Publishes an event, but does not save to object's eventstream.  It will be stored under out of band event stream so as to not pollute entity's
        /// </summary>
        protected void Raise<TEvent>(Action<TEvent> action, string id, IDictionary<string, string> metadata = null) where TEvent : IEvent
        {
            Logger.Write(LogLevel.Debug, () => $"Raising an OOB event {typeof(TEvent).FullName} on entity {GetType().FullName} stream [{Id}] bucket [{Bucket}]");
            var @event = EventFactory.CreateInstance(action);

            if (@event == null)
                throw new ArgumentException($"Failed to build event type {typeof(TEvent).FullName}");
            Raise(@event, id, metadata);
        }

        private void Apply(IEvent @event, IDictionary<string, string> metadata = null)
        {
            RouteFor(@event);
            
            // Todo: Fill with user headers or something
            Stream.Add(@event, metadata);
        }
        private void Raise(IEvent @event, string id, IDictionary<string, string> metadata = null)
        {
            // Todo: Fill metadata with user headers or something
            Stream.AddOob(@event, id, metadata);
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
