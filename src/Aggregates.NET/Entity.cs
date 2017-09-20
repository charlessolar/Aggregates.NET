using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Internal;
using Aggregates.Logging;
using Aggregates.Messages;

namespace Aggregates
{
    public abstract class Entity<TThis, TParent, TState> : Entity<TThis, TState>, IChildEntity<TParent> where TParent : IEntity where TThis : Entity<TThis, TParent, TState> where TState : IState, new()
    {
        TParent IChildEntity<TParent>.Parent => Parent;

        public TParent Parent { get; internal set; }
    }

    public abstract class Entity<TThis, TState> : IEntity<TState>, IHaveEntities<TThis>, INeedDomainUow, INeedEventFactory, INeedStore where TThis : Entity<TThis, TState> where TState : IState, new()
    {
        private static readonly ILog Logger = LogProvider.GetLogger(typeof(TThis).Name);

        public Id Id { get; private set; }
        public string Bucket { get; private set; }
        public Id[] Parents { get; private set; }
        public long Version { get; private set; }
        public TState State { get; private set; }

        public bool Dirty => Uncommitted.Any();

        public IFullEvent[] Uncommitted => _uncommitted.ToArray();


        private readonly IList<IFullEvent> _uncommitted = new List<IFullEvent>();

        private IDomainUnitOfWork Uow => (this as INeedDomainUow).Uow;
        private IEventFactory Factory => (this as INeedEventFactory).EventFactory;
        private IStoreEvents Store => (this as INeedStore).Store;
        IDomainUnitOfWork INeedDomainUow.Uow { get; set; }
        IEventFactory INeedEventFactory.EventFactory { get; set; }
        IStoreEvents INeedStore.Store { get; set; }

        void IEntity<TState>.Instantiate(TState state)
        {
            Id = state.Id;
            Bucket = state.Bucket;
            Parents = state.Parents;
            Version = state.Version;
            State = state;

            Instantiate(state);
        }

        protected virtual void Instantiate(TState state)
        {
        }


        public IRepository<TThis, TEntity> For<TEntity>() where TEntity : IChildEntity<TThis>
        {
            return Uow.For<TThis, TEntity>(this as TThis);
        }
        public IPocoRepository<TThis, T> Poco<T>() where T : class, new()
        {
            return Uow.Poco<TThis, T>(this as TThis);
        }
        public Task<long> GetSize(string oob = null)
        {
            var bucket = Bucket;
            if (!string.IsNullOrEmpty(oob))
                bucket = $"OOB-{oob}";

            return Store.Size<TThis>(bucket, Id, Parents);
        }

        public Task<IFullEvent[]> GetEvents(long start, int count, string oob = null)
        {
            var bucket = Bucket;
            if (!string.IsNullOrEmpty(oob))
                bucket = $"OOB-{oob}";

            return Store.GetEvents<TThis>(bucket, Id, Parents, start, count);
        }

        public Task<IFullEvent[]> GetEventsBackwards(long start, int count, string oob = null)
        {
            var bucket = Bucket;
            if (!string.IsNullOrEmpty(oob))
                bucket = $"OOB-{oob}";

            return Store.GetEventsBackwards<TThis>(bucket, Id, Parents, start, count);
        }

        void IEntity<TState>.Conflict(IEvent @event)
        {
            // if conflict handling fails it throws exception
            State.Conflict(@event);
            (this as IEntity<TState>).Apply(@event);
        }

        void IEntity<TState>.Apply(IEvent @event)
        {
            try
            {
                State.Apply(@event);
            }
            catch (NoRouteException)
            {
                Logger.Info($"{typeof(TThis).Name} missing state handler for event {@event.GetType().Name}");
            }
            _uncommitted.Add(new FullEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(TThis).AssemblyQualifiedName,
                    StreamType = StreamTypes.Domain,
                    Bucket = Bucket,
                    StreamId = Id,
                    Parents = Parents,
                    Timestamp = DateTime.UtcNow,
                    Version = State.Version,
                    Headers = new Dictionary<string, string>()
                },
                Event = @event
            });
        }

        void IEntity<TState>.Raise(IEvent @event, string id, bool transient, int? daysToLive)
        {
            _uncommitted.Add(new FullEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(TThis).AssemblyQualifiedName,
                    StreamType = StreamTypes.OOB,
                    Bucket = Bucket,
                    StreamId = Id,
                    Parents = Parents,
                    Timestamp = DateTime.UtcNow,
                    Version = State.Version,
                    Headers = new Dictionary<string, string>()
                    {
                        { Defaults.OobHeaderKey, id },
                        { Defaults.OobTransientKey, transient.ToString() },
                        { Defaults.OobDaysToLiveKey, daysToLive.ToString() }
                    }
                },
                Event = @event
            });
        }

        protected void Apply<TEvent>(Action<TEvent> @event) where TEvent : IEvent
        {
            var instance = Factory.Create(@event);

            (this as IEntity<TState>).Apply(instance);
        }

        protected void Raise<TEvent>(Action<TEvent> @event, string id, bool transient = false, int? daysToLive = null) where TEvent : IEvent
        {
            var instance = Factory.Create(@event);

            (this as IEntity<TState>).Raise(instance, id, transient, daysToLive);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }
    }
}
