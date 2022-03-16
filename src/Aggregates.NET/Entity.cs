using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using Aggregates.Messages;
using Microsoft.Extensions.Logging;

namespace Aggregates
{
    public abstract class Entity<TThis, TState, TParent> : Entity<TThis, TState>, IChildEntity<TParent> where TParent : IEntity where TThis : Entity<TThis, TState, TParent> where TState : class, IState, new()
    {
        IEntity IChildEntity.Parent => Parent;
        TParent IChildEntity<TParent>.Parent => Parent;

        public TParent Parent { get; internal set; }

    }

    public abstract class Entity<TThis, TState> : IEntity<TState>, IHaveEntities<TThis>, INeedDomainUow, INeedEventFactory, INeedStore, INeedVersionRegistrar, INeedChildTracking where TThis : Entity<TThis, TState> where TState : class, IState, new()
    {

        public static implicit operator TState(Entity<TThis, TState> entity) => entity?.State;

        public Id Id { get; private set; }
        public string Bucket { get; private set; }
        public long Version { get; private set; }
        public TState State { get; private set; }
        public long StateVersion => State.Version;

        public bool Dirty => Uncommitted.Any() || Version == EntityFactory.NewEntityVersion;

        public IFullEvent[] Uncommitted => _uncommitted.ToArray();


        private readonly IList<IFullEvent> _uncommitted = new List<IFullEvent>();

        private UnitOfWork.IDomainUnitOfWork Uow => (this as INeedDomainUow).Uow;
        private IEventFactory Factory => (this as INeedEventFactory).EventFactory;
        private IStoreEvents Store => (this as INeedStore).Store;
        private IVersionRegistrar VersionRegistrar => (this as INeedVersionRegistrar).Registrar;
        private ITrackChildren ChildrenTracker => (this as INeedChildTracking).Tracker;

        UnitOfWork.IDomainUnitOfWork INeedDomainUow.Uow { get; set; }
        IEventFactory INeedEventFactory.EventFactory { get; set; }
        IStoreEvents INeedStore.Store { get; set; }
        IVersionRegistrar INeedVersionRegistrar.Registrar { get; set; }
        ITrackChildren INeedChildTracking.Tracker { get; set; }

        internal ILogger Logger { get; set; }


        void IEntity<TState>.Instantiate(TState state)
        {
            Id = state.Id;
            Bucket = state.Bucket;
            Version = state.Version;
            State = state;

            Instantiate();
        }

        void IEntity<TState>.Snapshotting()
        {
            Snapshotting();
        }
        /// <summary>
        /// Allows the entity to perform any kind of initialization they may need to do (rare)
        /// </summary>
        protected virtual void Instantiate()
        {
        }

        /// <summary>
        /// Allows the entity to inject things into a serialized state object before its saved
        /// </summary>
        protected virtual void Snapshotting()
        {
        }


        public IRepository<TEntity, TThis> For<TEntity>() where TEntity : class, IChildEntity<TThis>
        {
            return Uow.For<TEntity, TThis>(this as TThis);
        }
        public Task<TEntity[]> Children<TEntity>() where TEntity : class, IChildEntity<TThis>
        {
            try
            {
                return ChildrenTracker.GetChildren<TEntity, TThis>(Uow, this as TThis);
            }
            catch
            {
                throw new InvalidOperationException("Failed to get children - perhaps children tracking is not enabled? ( Configure.SetTrackChildren )");
            }
        }

        void IEntity<TState>.Apply(IEvent @event)
        {
            try
            {
                State.Apply(@event);
            }
            catch (NoRouteException)
            {
                Logger?.DebugEvent("NoRoute", "{State} has no route for {EventType}", typeof(TThis).FullName, @event.GetType().FullName);
            }
            var newEvent = FullEventFactory.Event(VersionRegistrar, Uow, this, @event);
            _uncommitted.Add(newEvent);
        }


        /// <summary>
        /// Apply a new event to the stream, will be hydrated each future read
        /// </summary>
        protected void Apply<TEvent>(Action<TEvent> @event) where TEvent : IEvent
        {
            var instance = Factory.Create(@event);

            (this as IEntity<TState>).Apply(instance);
        }


        /// <summary>
        /// A rule for throwing business exceptions based on state
        /// </summary>
        /// <param name="name"></param>
        /// <param name="expression">returns TRUE if should throw</param>
        /// <param name="message"></param>
        public void Rule(string name, Func<TState, bool> expression, string message = "")
        {
            if (expression(State))
            {
                if (string.IsNullOrEmpty(message))
                    throw new BusinessException(name);
                else
                    throw new BusinessException(name, message);
            }
        }

        public override string ToString()
        {
            //var parents = Parents != null && Parents.Any() ? $" [{Parents.BuildParentsString()}] " : " ";
            return $"{typeof(TThis).FullName} [{Bucket}] [{Id}] v{Version}({_uncommitted.Count})";
        }
    }
}
