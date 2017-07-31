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

namespace Aggregates
{
    public abstract class State<TThis, TParent> : State<TThis>, IEntity<TParent> where TParent : State<TParent> where TThis : State<TThis, TParent>
    {
        IEventSource IEventSource.Parent => Parent;

        public TParent Parent { get; internal set; }
    }
    public abstract class State<TThis> : IEntity, INeedRouteResolver, INeedStream, INeedBuilder where TThis : State<TThis>
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(TThis).Name);

        internal IBuilder Builder => (this as INeedBuilder).Builder;
        internal IEventStream Stream => (this as INeedStream).Stream;
        internal IRouteResolver Resolver => (this as INeedRouteResolver).Resolver;

        Id IEventSource.Id => Id;
        long IEventSource.Version => Version;
        IEventSource IEventSource.Parent => null;

        public Id Id => Stream.StreamId;
        public string Bucket => Stream.Bucket;
        public long Version => Stream.StreamVersion;
        public long CommitVersion => Stream.CommitVersion;
        

        IEventStream INeedStream.Stream { get; set; }
        IEventStream IEventSource.Stream => (this as INeedStream).Stream;
        
        IRouteResolver INeedRouteResolver.Resolver { get; set; }


        IBuilder INeedBuilder.Builder { get; set; }


        public IRepository<TThis, TEntity> For<TEntity>() where TEntity : State<TEntity, TThis>
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

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        void IEventSource.Hydrate(IEnumerable<IEvent> events)
        {
            Logger.Write(LogLevel.Debug, () => $"Hydrating {events.Count()} events to entity {GetType().FullName} stream [{Id}] bucket [{Bucket}]");
            foreach (var @event in events)
                RouteFor(@event);
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
    }
}
