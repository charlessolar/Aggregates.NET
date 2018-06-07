using Aggregates.Contracts;
using Aggregates.Exceptions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates.Internal
{
    class EventPlanner<TEntity, TState> : IEventPlanner<TEntity> where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private TestableUnitOfWork _uow;
        private TestableEventStore _events;
        private TestableSnapshotStore _snapshots;
        private TestableEventFactory _factory;
        private Func<TEntity> _entityFactory;
        private string _bucket;
        private TestableId _id;
        private Id[] _parents;

        public EventPlanner(TestableUnitOfWork uow, TestableEventStore events, TestableSnapshotStore snapshots, TestableEventFactory factory, Func<TEntity> entityFactory, string bucket, TestableId id, Id[] parents = null)
        {
            _uow = uow;
            _events = events;
            _snapshots = snapshots;
            _factory = factory;
            _entityFactory = entityFactory;
            _bucket = bucket;
            _id = id;
            _parents = parents ?? new Id[] { };
        }

        public IEventPlanner<TEntity> Exists()
        {
            _events.Exists<TEntity>(_bucket, _id, _parents);
            return this;
        }
        public IEventPlanner<TEntity> HasEvent<TEvent>(Action<TEvent> factory)
        {
            _events.AddEvent<TEntity>(_bucket, _id, _parents, (Messages.IEvent)_factory.Create(factory));
            return this;
        }
        public IEventPlanner<TEntity> HasSnapshot(object snapshot)
        {
            _snapshots.SpecifySnapshot<TState>(_bucket, _id, snapshot);
            return this;
        }
        public IEventPlanner<TChild> Plan<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>
        {
            // Use a factory so its 'lazy' - meaning defining the parent doesn't necessarily have to come before defining child
            return _uow.Plan<TChild, TEntity>(_entityFactory(), _uow.MakeId(id.ToString()));
        }
        public IEventPlanner<TChild> Plan<TChild>(TestableId id) where TChild : IEntity, IChildEntity<TEntity>
        {
            // Use a factory so its 'lazy' - meaning defining the parent doesn't necessarily have to come before defining child
            return _uow.Plan<TChild, TEntity>(_entityFactory(), id);
        }

    }
    class Checker<TEntity, TState> : IChecker<TEntity> where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private TestableUnitOfWork _uow;
        private TestableEventFactory _factory;
        private TEntity _entity;

        public Checker(TestableUnitOfWork uow, TestableEventFactory factory, TEntity entity)
        {
            _uow = uow;
            _factory = factory;
            _entity = entity;
        }

        public IChecker<TEntity> Raised<TEvent>(Action<TEvent> factory) where TEvent : Messages.IEvent
        {
            var @event = _factory.Create(factory);

            if (!_entity.Uncommitted.Any(x => JsonConvert.SerializeObject(x.Event) == JsonConvert.SerializeObject(@event)))
                throw new DidNotRaisedException(@event, _entity.Uncommitted.Select(x => x.Event as Messages.IEvent).ToArray());

            return this;
        }
        public IChecker<TEntity> Raised<TEvent>() where TEvent : Messages.IEvent
        {
            if(!_entity.Uncommitted.Select(x => x.Event as Messages.IEvent).OfType<TEvent>().Any())
                throw new NoMatchingEventException(_entity.Uncommitted.Select(x => x.Event as Messages.IEvent).ToArray());
            return this;
        }
        public IChecker<TEntity> Raised<TEvent>(Func<TEvent, bool> assert) where TEvent : Messages.IEvent
        {
            if (!_entity.Uncommitted.Select(x => x.Event as Messages.IEvent).OfType<TEvent>().Any(assert))
                throw new NoMatchingEventException(_entity.Uncommitted.Select(x => x.Event as Messages.IEvent).ToArray());
            return this;
        }
        public IChecker<TChild> Check<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>
        {
            return _uow.Check<TChild, TEntity>(_entity, _uow.MakeId(id.ToString()));
        }
        public IChecker<TChild> Check<TChild>(TestableId id) where TChild : IEntity, IChildEntity<TEntity>
        {
            return _uow.Check<TChild, TEntity>(_entity, id);
        }
    }
    class PocoPlanner<T> : IPocoPlanner where T : class, new()
    {
        private readonly TestablePocoRepository<T> _repo;
        private readonly string _bucket;
        private readonly TestableId _id;
        private readonly Id[] _parents;

        public PocoPlanner(TestablePocoRepository<T> repo, string bucket, TestableId id, Id[] parents = null)
        {
            _repo = repo;
            _bucket = bucket;
            _id = id;
            _parents = parents ?? new Id[] { };
        }

        public IPocoPlanner HasValue(object poco)
        {
            _repo.DefinePoco(_bucket, (Id)_id, _parents, poco as T);
            return this;
        }
    }
    class PocoChecker<T> : IPocoChecker where T : class, new()
    {
        private readonly TestablePocoRepository<T> _repo;
        private readonly object _poco;

        public PocoChecker(TestablePocoRepository<T> repo, object poco)
        {
            _repo = repo;
            _poco = poco;
        }

        public IPocoChecker IsEqual(object poco)
        {
            if (JsonConvert.SerializeObject(_poco) != JsonConvert.SerializeObject(poco))
                throw new PocoUnequalException(_poco, poco);
            return this;
        }

    }
}
