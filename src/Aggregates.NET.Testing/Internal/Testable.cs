using Aggregates.Contracts;
using Aggregates.Exceptions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class EventPlanner<TEntity, TState> : IEventPlanner<TEntity> where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private IdRegistry _ids;
        private TestableDomain _uow;
        private TestableEventStore _events;
        private TestableSnapshotStore _snapshots;
        private TestableEventFactory _factory;
        private Func<TEntity> _entityFactory;
        private string _bucket;
        private TestableId _id;
        private Id[] _parents;

        public EventPlanner(TestableDomain uow, IdRegistry ids, TestableEventStore events, TestableSnapshotStore snapshots, TestableEventFactory factory, Func<TEntity> entityFactory, string bucket, TestableId id, Id[] parents = null)
        {
            _ids = ids;
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
            return _uow.Plan<TChild, TEntity>(_entityFactory(), _ids.MakeId(id.ToString()));
        }
        public IEventPlanner<TChild> Plan<TChild>(TestableId id) where TChild : IEntity, IChildEntity<TEntity>
        {
            // Use a factory so its 'lazy' - meaning defining the parent doesn't necessarily have to come before defining child
            return _uow.Plan<TChild, TEntity>(_entityFactory(), id);
        }

    }
    [ExcludeFromCodeCoverage]
    class EventChecker<TEntity, TState> : IEventChecker<TEntity> where TEntity : Entity<TEntity, TState> where TState : class, IState, new()
    {
        private IdRegistry _ids;
        private TestableDomain _uow;
        private TestableEventFactory _factory;
        private TEntity _entity;

        public EventChecker(TestableDomain uow, IdRegistry ids, TestableEventFactory factory, TEntity entity)
        {
            _ids = ids;
            _uow = uow;
            _factory = factory;
            _entity = entity;
        }

        public IEventChecker<TEntity> Raised<TEvent>(Action<TEvent> factory) where TEvent : Messages.IEvent
        {
            var @event = _factory.Create(factory);

            if (!_entity.Uncommitted.Any(x => JsonConvert.SerializeObject(x.Event) == JsonConvert.SerializeObject(@event)))
                throw new DidNotRaisedException(@event, _entity.Uncommitted.Select(x => x.Event as Messages.IEvent).ToArray());

            return this;
        }
        public IEventChecker<TEntity> Raised<TEvent>() where TEvent : Messages.IEvent
        {
            if(!_entity.Uncommitted.Select(x => x.Event as Messages.IEvent).OfType<TEvent>().Any())
                throw new NoMatchingEventException(_entity.Uncommitted.Select(x => x.Event as Messages.IEvent).ToArray());
            return this;
        }
        public IEventChecker<TEntity> Raised<TEvent>(Func<TEvent, bool> assert) where TEvent : Messages.IEvent
        {
            if (!_entity.Uncommitted.Select(x => x.Event as Messages.IEvent).OfType<TEvent>().Any(assert))
                throw new NoMatchingEventException(_entity.Uncommitted.Select(x => x.Event as Messages.IEvent).ToArray());
            return this;
        }
        public IEventChecker<TChild> Check<TChild>(Id id) where TChild : IEntity, IChildEntity<TEntity>
        {
            return _uow.Check<TChild, TEntity>(_entity, _ids.MakeId(id.ToString()));
        }
        public IEventChecker<TChild> Check<TChild>(TestableId id) where TChild : IEntity, IChildEntity<TEntity>
        {
            return _uow.Check<TChild, TEntity>(_entity, id);
        }
    }
    [ExcludeFromCodeCoverage]
    class ModelChecker<TModel> : IModelChecker<TModel> where TModel : class, new()
    {
        private TestableApplication _app;
        private IdRegistry _ids;
        private TestableId _id;

        public ModelChecker(TestableApplication app, IdRegistry ids, Id id)
        {
            _app = app;
            _ids = ids;
            _id = _ids.MakeId(id);
        }

        public IModelChecker<TModel> Added()
        {
            if (!_app.Added.ContainsKey(_id))
                throw new ModelException<TModel>(_id, "added");
            return this;
        }

        public IModelChecker<TModel> Added(Func<TModel, bool> assert)
        {
            if (!_app.Added.ContainsKey(_id))
                throw new ModelException<TModel>(_id, "added");
            var model = _app.Added[_id] as TModel;

            if (!assert(model))
                throw new ModelException<TModel>(_id, model);
            return this;
        }

        public IModelChecker<TModel> Added(TModel check)
        {
            if (!_app.Added.ContainsKey(_id))
                throw new ModelException<TModel>(_id, "added");
            var model = _app.Added[_id] as TModel;

            if (JsonConvert.SerializeObject(model) != JsonConvert.SerializeObject(check))
                throw new ModelException<TModel>(_id, model);
            return this;
        }

        public IModelChecker<TModel> Deleted()
        {
            if (!_app.Deleted.Contains(_id))
                throw new ModelException<TModel>(_id, "deleted");
            return this;
        }

        public IModelChecker<TModel> Read()
        {
            if (!_app.Read.Contains(_id))
                throw new ModelException<TModel>(_id, "read");
            return this;
        }

        public IModelChecker<TModel> Updated()
        {
            if (!_app.Updated.ContainsKey(_id))
                throw new ModelException<TModel>(_id, "updated");
            return this;
        }

        public IModelChecker<TModel> Updated(Func<TModel, bool> assert)
        {
            if (!_app.Updated.ContainsKey(_id))
                throw new ModelException<TModel>(_id, "updated");
            var model = _app.Updated[_id] as TModel;

            if (!assert(model))
                throw new ModelException<TModel>(_id, model);
            return this;
        }

        public IModelChecker<TModel> Updated(TModel check)
        {
            if (!_app.Updated.ContainsKey(_id))
                throw new ModelException<TModel>(_id, "updated");
            var model = _app.Updated[_id] as TModel;

            if (JsonConvert.SerializeObject(model) != JsonConvert.SerializeObject(check))
                throw new ModelException<TModel>(_id, model);
            return this;
        }
    }

    [ExcludeFromCodeCoverage]
    class ModelPlanner<TModel> : IModelPlanner<TModel> where TModel : class, new()
    {
        private TestableApplication _app;
        private IdRegistry _ids;
        private TestableId _id;

        public ModelPlanner(TestableApplication app, IdRegistry ids, Id id)
        {
            _app = app;
            _ids = ids;
            _id = _ids.MakeId(id);
        }

        public IModelPlanner<TModel> Exists()
        {
            _app.Planned[_id] = new TModel();
            return this;
        }

        public IModelPlanner<TModel> Exists(TModel model)
        {
            _app.Planned[_id] = model;
            return this;
        }
    }

    [ExcludeFromCodeCoverage]
    class ServicePlanner<TService, TResponse> : IServicePlanner<TService, TResponse> where TService : IService<TResponse>
    {
        private TestableProcessor _processor;
        private TService _service;

        public ServicePlanner(TestableProcessor processor, TService service)
        {
            _processor = processor;
            _service = service;
        }

        public IServicePlanner<TService, TResponse> Response(TResponse response)
        {
            _processor.Planned[JsonConvert.SerializeObject(_service)] = response;
            return this;
        }
    }

    [ExcludeFromCodeCoverage]
    class ServiceChecker<TService, TResponse> : IServiceChecker<TService, TResponse> where TService : IService<TResponse>
    {
        private TestableProcessor _processor;
        private TService _service;

        public ServiceChecker(TestableProcessor processor, TService service)
        {
            _processor = processor;
            _service = service;
        }

        public IServiceChecker<TService, TResponse> Requested()
        {
            if (!_processor.Requested.Contains(JsonConvert.SerializeObject(_service)))
                throw new ServiceException<TService>(JsonConvert.SerializeObject(_service));
            return this;
        }
    }
}
