using Aggregates.Contracts;
using Aggregates.Exceptions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Aggregates.Internal
{
    // inspired / taken from NEventStore.CommonDomain
    // https://github.com/NEventStore/NEventStore/blob/master/src/NEventStore/CommonDomain/Persistence/EventStore/EventStoreRepository.cs

    public class Repository<T> : IRepository<T> where T : class, IEntity
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Repository<>));
        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapstore;
        private readonly IBuilder _builder;
        
        protected readonly ConcurrentDictionary<String, T> _tracked = new ConcurrentDictionary<String, T>();

        private Boolean _disposed;

        public Repository(IBuilder builder)
        {
            _builder = builder;
            _store = _builder.Build<IStoreEvents>();
            _snapstore = _builder.Build<IStoreSnapshots>();
        }

        void IRepository.Commit(Guid commitId, IDictionary<String, String> headers)
        {
            foreach (var tracked in _tracked.Values )
            {
                var stream = tracked.Stream;

                if (tracked is ISnapshotting && (tracked as ISnapshotting).ShouldTakeSnapshot())
                {
                    Logger.DebugFormat("Taking snapshot of {0} id {1} version {2}", tracked.GetType().FullName, tracked.StreamId, tracked.Version);
                    var memento = (tracked as ISnapshotting).TakeSnapshot();
                    stream.AddSnapshot(memento, headers);
                }


                var count = 0;
                var success = false;
                do
                {
                    try
                    {
                        count++;
                        stream.Commit(commitId, headers);
                        success = true;
                    }
                    catch (VersionException version)
                    {
                        try
                        {
                            Logger.DebugFormat("Stream {0} entity {1} has version conflicts with store - attempting to resolve", tracked.StreamId, tracked.GetType().FullName);
                            stream = ResolveConflict(tracked.Stream);
                        }
                        catch
                        {
                            Logger.ErrorFormat("Stream {0} entity {1} has version conflicts with store - FAILED to resolve", tracked.StreamId, tracked.GetType().FullName);
                            throw new ConflictingCommandException("Could not resolve conflicting events", version);
                        }
                    }
                    catch (DuplicateCommitException)
                    {
                        Logger.WarnFormat("Detected a possible double commit for stream: {0} bucket {1}", stream.StreamId, stream.Bucket);
                    }
                } while (!success && count < 5);
            }
        }

        private IEventStream ResolveConflict(IEventStream stream)
        {
            var uncommitted = stream.Uncommitted;
            // Get latest stream from store
            var existing = Get(stream.Bucket, stream.StreamId);
            // Hydrate the uncommitted events
            existing.Hydrate(uncommitted);
            // Success! Streams merged
            return existing.Stream;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            _tracked.Clear();

            _disposed = true;
        }

        public virtual T Get<TId>(TId id)
        {
            return Get<TId>(Bucket.Default, id);
        }

        public T Get<TId>(String bucket, TId id)
        {
            Logger.DebugFormat("Retreiving aggregate id '{0}' from bucket '{1}' in store", id, bucket);
            var root = Get(bucket, id.ToString());
            (root as IEventSource<TId>).Id = id;
            return root;
        }
        public T Get(String bucket, String id)
        {
            var cacheId = String.Format("{0}.{1}", bucket, id);
            return _tracked.GetOrAdd(cacheId, (key) =>
            {
                var snapshot = GetSnapshot(bucket, id);
                var stream = OpenStream(bucket, id, snapshot);

                if (stream == null && snapshot == null)
                    throw new NotFoundException("Aggregate snapshot not found");

                // Get requires the stream exists
                if (stream.StreamVersion == -1)
                    throw new NotFoundException("Aggregate stream not found");

                // Call the 'private' constructor
                var root = Newup(stream, _builder);

                if (snapshot != null && root is ISnapshotting)
                    ((ISnapshotting)root).RestoreSnapshot(snapshot.Payload);

                (root as IEventSource).Hydrate(stream.Events.Select(e => e.Event));

                return root;
            });
        }

        public virtual T New<TId>(TId id)
        {
            return New<TId>(Bucket.Default, id);
        }

        public T New<TId>(String bucket, TId id)
        {
            var root = New(bucket, id.ToString());
            (root as IEventSource<TId>).Id = id;

            return root;
        }
        public T New(String bucket, String streamId)
        {
            var stream = OpenStream(bucket, streamId);
            var root = Newup(stream, _builder);

            var cacheId = String.Format("{0}.{1}", bucket, streamId);
            _tracked.TryAdd(cacheId, root);
            return root;
        }

        protected T Newup(IEventStream stream, IBuilder builder)
        {
            // Call the 'private' constructor
            var tCtor = typeof(T).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { }, null);

            if (tCtor == null)
                throw new AggregateException("Aggregate needs a PRIVATE parameterless constructor");
            var root = (T)tCtor.Invoke(null);

            // Todo: I bet there is a way to make a INeedBuilding<T> type interface
            //      and loop over each, calling builder.build for each T
            if (root is INeedStream)
                (root as INeedStream).Stream = stream;
            if (root is INeedBuilder)
                (root as INeedBuilder).Builder = builder;
            if (root is INeedEventFactory)
                (root as INeedEventFactory).EventFactory = builder.Build<IMessageCreator>();
            if (root is INeedRouteResolver)
                (root as INeedRouteResolver).Resolver = builder.Build<IRouteResolver>();
            if (root is INeedRepositoryFactory)
                (root as INeedRepositoryFactory).RepositoryFactory = builder.Build<IRepositoryFactory>();
            if (root is INeedProcessor)
                (root as INeedProcessor).Processor = builder.Build<IProcessor>();

            return root;
        }
        
        protected ISnapshot GetSnapshot(String bucket, String streamId)
        {
            return _snapstore.GetSnapshot(bucket, streamId);
        }

        protected IEventStream OpenStream(String bucket, String streamId, ISnapshot snapshot = null)
        {
            return _store.GetStream<T>(bucket, streamId, snapshot?.Version + 1);
        }
        
    }
}