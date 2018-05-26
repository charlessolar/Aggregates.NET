using Aggregates.Contracts;
using Aggregates.Exceptions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates.Internal
{
    class EventPlanner<TState> : IEventPlanner where TState : class, IState, new()
    {
        private TestableEventStore _events;
        private TestableSnapshotStore _snapshots;
        private TestableEventFactory _factory;
        private string _bucket;
        private Id _id;
        private Id[] _parents;

        public EventPlanner(TestableEventStore events, TestableSnapshotStore snapshots, TestableEventFactory factory, string bucket, Id id, Id[] parents = null)
        {
            _events = events;
            _snapshots = snapshots;
            _factory = factory;
            _bucket = bucket;
            _id = id;
            _parents = parents ?? new Id[] { };
        }

        public IEventPlanner HasEvent<TEvent>(Action<TEvent> factory)
        {
            _events.AddEvent(_bucket, _id, _parents, (Messages.IEvent)_factory.Create(factory));
            return this;
        }
        public IEventPlanner HasSnapshot(object snapshot)
        {
            _snapshots.SpecifySnapshot<TState>(_bucket, _id, snapshot);
            return this;
        }
    }
    class Checker<TEntity, TState> : IChecker where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private TestableEventFactory _factory;
        private TEntity _entity;

        public Checker(TestableEventFactory factory, TEntity entity)
        {
            _factory = factory;
            _entity = entity;
        }

        public IChecker Raised<TEvent>(Action<TEvent> factory) where TEvent : Messages.IEvent
        {
            var @event = _factory.Create(factory);

            if (!_entity.Uncommitted.Any(x => JsonConvert.SerializeObject(x.Event) == JsonConvert.SerializeObject(@event)))
                throw new DidNotRaisedException(@event, _entity.Uncommitted.Select(x => x.Event as Messages.IEvent).ToArray());

            return this;
        }
    }
    class PocoPlanner<T> : IPocoPlanner where T : class, new()
    {
        private readonly TestablePocoRepository<T> _repo;
        private readonly string _bucket;
        private readonly Id _id;
        private readonly Id[] _parents;

        public PocoPlanner(TestablePocoRepository<T> repo, string bucket, Id id, Id[] parents = null)
        {
            _repo = repo;
            _bucket = bucket;
            _id = id;
            _parents = parents ?? new Id[] { };
        }

        public IPocoPlanner HasValue(object poco)
        {
            _repo.Pocos.Add(Tuple.Create(_bucket, _id, _parents), poco as T);
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
