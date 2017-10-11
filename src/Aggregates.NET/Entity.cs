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
    public abstract class Entity<TThis, TState, TParent> : Entity<TThis, TState>, IChildEntity<TParent> where TParent : IEntity where TThis : Entity<TThis, TState, TParent> where TState : class, IState, new()
    {
        TParent IChildEntity<TParent>.Parent => Parent;

        public TParent Parent { get; internal set; }
    }

    public abstract class Entity<TThis, TState> : IEntity<TState>, IHaveEntities<TThis>, INeedDomainUow, INeedEventFactory, INeedStore where TThis : Entity<TThis, TState> where TState : class, IState, new()
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
        private IOobWriter OobWriter => (this as INeedStore).OobWriter;
        IDomainUnitOfWork INeedDomainUow.Uow { get; set; }
        IEventFactory INeedEventFactory.EventFactory { get; set; }
        IStoreEvents INeedStore.Store { get; set; }
        IOobWriter INeedStore.OobWriter { get; set; }

        void IEntity<TState>.Instantiate(TState state)
        {
            Id = state.Id;
            Bucket = state.Bucket;
            Parents = state.Parents;
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


        public IRepository<TEntity, TThis> For<TEntity>() where TEntity : IChildEntity<TThis>
        {
            return Uow.For<TEntity, TThis>(this as TThis);
        }
        public IPocoRepository<T, TThis> Poco<T>() where T : class, new()
        {
            return Uow.Poco<T, TThis>(this as TThis);
        }
        public Task<long> GetSize(string oob = null)
        {
            if (!string.IsNullOrEmpty(oob))
                return OobWriter.GetSize<TThis>(Bucket, Id, Parents, oob);

            return Store.Size<TThis>(Bucket, Id, Parents);
        }

        public Task<IFullEvent[]> GetEvents(long start, int count, string oob = null)
        {
            if (!string.IsNullOrEmpty(oob))
                return OobWriter.GetEvents<TThis>(Bucket, Id, Parents, oob, start, count);

            return Store.GetEvents<TThis>(Bucket, Id, Parents, start, count);
        }

        public Task<IFullEvent[]> GetEventsBackwards(long start, int count, string oob = null)
        {
            if (!string.IsNullOrEmpty(oob))
                return OobWriter.GetEventsBackwards<TThis>(Bucket, Id, Parents, oob, start, count);

            return Store.GetEventsBackwards<TThis>(Bucket, Id, Parents, start, count);
        }

        void IEntity<TState>.Conflict(IEvent @event)
        {
            // if conflict handling fails it throws exception
            State.Conflict(@event);
            (this as IEntity<TState>).Apply(@event);
        }

        void IEntity<TState>.Apply(IEvent @event)
        {
            State.Apply(@event);
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

        protected void Raise<TEvent>(Action<TEvent> @event, string id, bool transient = true, int? daysToLive = null) where TEvent : IEvent
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
