using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class EntityRepository<TAggregateId, T> : IEntityRepository<TAggregateId, T> where T : class, IEntity
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EntityRepository<,>));
        private readonly IEventStream _stream;
        private readonly IBuilder _builder;
        private readonly TAggregateId _aggregateId;

        public EntityRepository(TAggregateId AggregateId, IBuilder builder, IEventStream stream)
        {
            _aggregateId = AggregateId;
            _builder = builder;
            _stream = stream;
        }

        public T Get<TId>(TId id)
        {
            return Get(id, Int32.MaxValue);
        }

        public T Get<TId>(TId id, int version)
        {
            Logger.DebugFormat("Retreiving entity id {0} version {1} from aggregate {2} in store", id, version, _stream.StreamId);

            var events = GetEntityStream(id, version);

            if (events.Count() == 0) return (T)null;

            // Call the 'private' constructor
            var entity = Newup(_builder);
            (entity as IEventSource<TId>).Id = id;
            (entity as IEntity<TId, TAggregateId>).AggregateId = _aggregateId;

            // Todo: support entity snapshots
            //if (snapshot != null && root is ISnapshotting)
            //    ((ISnapshotting)root).RestoreSnapshot(snapshot);

            if (version == 0 || entity.Version < version)
                entity.Hydrate(events.Select(e => e.Body));

            return entity;
        }

        public T New<TId>(TId id)
        {
            PrepareStream(id);
            var entity = Newup(_builder);
            (entity as IEventSource<TId>).Id = id;
            return entity;
        }

        private T Newup(IBuilder builder)
        {
            // Call the 'private' constructor
            var tCtor = typeof(T).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { }, null);

            if (tCtor == null)
                throw new AggregateException("Entity needs a PRIVATE parameterless constructor");
            var entity = (T)tCtor.Invoke(null);

            // Todo: I bet there is a way to make a INeedBuilding<T> type interface
            //      and loop over each, calling builder.build for each T
            if (entity is INeedStream)
                (entity as INeedStream).Stream = _stream;
            if (entity is INeedBuilder)
                (entity as INeedBuilder).Builder = builder;
            if (entity is INeedEventFactory)
                (entity as INeedEventFactory).EventFactory = builder.Build<IMessageCreator>();
            if (entity is INeedRouteResolver)
                (entity as INeedRouteResolver).Resolver = builder.Build<IRouteResolver>();
            if (entity is INeedRepositoryFactory)
                (entity as INeedRepositoryFactory).RepositoryFactory = builder.Build<IRepositoryFactory>();

            return entity;
        }

        private IEnumerable<EventMessage> GetEntityStream<TId>(TId id, Int32 version)
        {
            // The stream is the aggregate's entire stream.  For the entity, we only need the events for our id
            return (_stream.CommittedEvents.Concat(_stream.UncommittedEvents)).Where(e =>
            {
                Object objId;
                if (!e.Headers.TryGetValue("Id", out objId))
                    return false;
                return id.Equals(objId);
            }).Take(version);
        }

        private void PrepareStream<TId>(TId id)
        {
            // Insert a blank 'created' event onto aggregate stream

            _stream.Add(new EventMessage
            {
                Body = null,
                Headers = new Dictionary<string, object>
                {
                    { "Id", id },
                    { "Entity", typeof(T).FullName },
                    { "Event", "Entity Creation" },
                    { "EventVersion", _stream.StreamRevision }
                    // Todo: Support user headers via method or attributes
                }
            });
        }
    }
}