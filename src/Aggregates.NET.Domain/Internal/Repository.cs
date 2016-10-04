using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    // inspired / taken from NEventStore.CommonDomain
    // https://github.com/NEventStore/NEventStore/blob/master/src/NEventStore/CommonDomain/Persistence/EventStore/EventStoreRepository.cs

    // Todo: The hoops we jump through to support <TId> can be simplified by just using an Id class with implicit converters from string, int, guid, etc.

    public class Repository<T> : IRepository<T> where T : class, IEventSource
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Repository<>));
        private readonly IStoreEvents _store;
        private readonly IStoreSnapshots _snapstore;
        private readonly IBuilder _builder;
        private readonly ReadOnlySettings _settings;

        private static Histogram WrittenEvents = Metric.Histogram("Written Events", Unit.Events);
        private static Meter Conflicts = Metric.Meter("Conflicts", Unit.Items);
        private static Meter ConflictsResolved = Metric.Meter("Conflicts Resolved", Unit.Items);
        private static Meter ConflictsUnresolved = Metric.Meter("Conflicts Unesolved", Unit.Items);
        private static Meter WriteErrors = Metric.Meter("Event Write Errors", Unit.Errors);

        protected readonly IDictionary<String, T> _tracked = new Dictionary<String, T>();

        private Boolean _disposed;

        public Repository(IBuilder builder)
        {
            _builder = builder;
            _snapstore = _builder.Build<IStoreSnapshots>();
            _store = _builder.Build<IStoreEvents>();
            _settings = _builder.Build<ReadOnlySettings>();
            _store.Builder = _builder;
        }

        async Task IRepository.Commit(Guid commitId, IDictionary<String, String> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(T).FullName} starting commit {commitId}");
            var written = 0;
            await _tracked.Values
                .Where(x => x.Stream.Uncommitted.Any())
                .WhenAllAsync(async (tracked) =>
            {
                var headers = new Dictionary<String, String>(commitHeaders);

                var stream = tracked.Stream;

                if (stream.StreamVersion != stream.CommitVersion && tracked is ISnapshotting && (tracked as ISnapshotting).ShouldTakeSnapshot())
                {
                    Logger.Write(LogLevel.Debug, () => $"Taking snapshot of {tracked.GetType().FullName} id [{tracked.StreamId}] version {tracked.Version}");
                    var memento = (tracked as ISnapshotting).TakeSnapshot();
                    stream.AddSnapshot(memento, headers);
                }

                Interlocked.Add(ref written, stream.Uncommitted.Count());

                var conflictRetries = _settings.Get<Int32>("MaxConflictResolves");

                var success = false;
                var sanity = Math.Max(conflictRetries, 5);
                do
                {
                    try
                    {
                        await stream.Commit(commitId, headers).ConfigureAwait(false);
                        success = true;
                    }
                    catch (VersionException)
                    {

                        try
                        {
                            Conflicts.Mark();

                            if (conflictRetries <= 0)
                                throw new Exception($"Stream [{tracked.StreamId}] entity {tracked.GetType().FullName} version {tracked.Version} has version conflicts with store - ran out of retries!");

                            conflictRetries--;
                            Logger.WriteFormat(LogLevel.Debug, "Stream [{0}] entity {1} version {2} has version conflicts with store - attempting to resolve", tracked.StreamId, tracked.GetType().FullName, tracked.Version);
                            stream = await ResolveConflict(tracked).ConfigureAwait(false);
                            Logger.WriteFormat(LogLevel.Debug, "Stream [{0}] entity {1} version {2} has version conflicts with store - successfully resolved", tracked.StreamId, tracked.GetType().FullName, tracked.Version);
                            ConflictsResolved.Mark();
                        }
                        catch (AbandonConflictException abandon)
                        {
                            ConflictsUnresolved.Mark();
                            Logger.WriteFormat(LogLevel.Error, "Stream [{0}] entity {1} has version conflicts with store - FAILED to resolve", tracked.StreamId, tracked.GetType().FullName);
                            throw new ConflictingCommandException("Could not resolve conflicting events", abandon);
                        }
                        
                    }
                    catch (PersistenceException e)
                    {
                        WriteErrors.Mark();
                        Logger.WriteFormat(LogLevel.Warn, "Failed to commit events to store for stream: [{0}] bucket [{1}]\nException: {2}", stream.StreamId, stream.Bucket, e);
                    }
                    catch (DuplicateCommitException)
                    {
                        WriteErrors.Mark();
                        Logger.WriteFormat(LogLevel.Warn, "Detected a double commit for stream: [{0}] bucket [{1}] - discarding changes for this stream", stream.StreamId, stream.Bucket);
                        // I was throwing this, but if this happens it means the events for this message have already been committed.  Possibly as a partial message failure earlier. 
                        // Im changing to just discard the changes, perhaps can take a deeper look later if this ever bites me on the ass
                        //throw;
                        success = true;
                    }
                    catch
                    {
                        WriteErrors.Mark();
                        throw;
                    }
                    sanity--;
                } while (!success && sanity >= 0);
            });

            WrittenEvents.Update(written);
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(T).FullName} finished commit {commitId}");
        }

        private async Task<IEventStream> ResolveConflict(T tracked)
        {
            var stream = tracked.Stream;
            var uncommitted = stream.Uncommitted.Select(x => x.Event).ToList();

            // Our cache is out of date
            await _store.Evict<T>(stream.Bucket, stream.StreamId);
            await _snapstore.Evict<T>(stream.Bucket, stream.StreamId);

            Logger.Write(LogLevel.Debug, () => $"Resolving - getting stream {stream.StreamId} bucket {stream.Bucket} from store");
            // Get latest stream from store
            var existing = await GetUntracked(stream.Bucket, stream.StreamId).ConfigureAwait(false);
            Logger.Write(LogLevel.Debug, () => $"Resolving - got stream version {existing.Version} from store, hydrating {stream.Uncommitted.Count()} uncomitted events");

            // Hydrate events using special `Conflict` handlers the user can specify to help auto resolve conflicts
            // if a conflict handler doesn't exist it will throw `NoRouteException`, if the user aborts they'll throw `AbandonConflictException`
            existing.Conflict(uncommitted);

            foreach (var oob in stream.OOBUncommitted)
                existing.Raise(oob.Event);
            
            Logger.WriteFormat(LogLevel.Debug, "Resolving - successfully hydrated");
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

        public virtual Task<T> TryGet<TId>(TId id)
        {
            return TryGet<TId>(Defaults.Bucket, id);
        }
        public async Task<T> TryGet<TId>(String bucket, TId id)
        {
            if (id == null) return null;
            if (typeof(TId) == typeof(String) && String.IsNullOrEmpty(id as String)) return null;
            try
            {
                return await Get<TId>(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return null;
        }

        public virtual Task<T> Get<TId>(TId id)
        {
            return Get<TId>(Defaults.Bucket, id);
        }

        public async Task<T> Get<TId>(String bucket, TId id)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving aggregate id [{id}] in bucket [{bucket}] for type {typeof(T).FullName} in store");
            var root = await Get(bucket, id.ToString()).ConfigureAwait(false);
            (root as IEventSource<TId>).Id = id;
            return root;
        }
        public async Task<T> Get(String bucket, String id)
        {
            var cacheId = String.Format("{0}.{1}", bucket, id);
            T root;
            if (!_tracked.TryGetValue(cacheId, out root))
                _tracked[cacheId] = root = await GetUntracked(bucket, id).ConfigureAwait(false);

            return root;
        }
        private async Task<T> GetUntracked(String bucket, string streamId)
        {
            T root;
            var snapshot = await _snapstore.GetSnapshot<T>(bucket, streamId).ConfigureAwait(false);
            var stream = await _store.GetStream<T>(bucket, streamId, snapshot?.Version + 1).ConfigureAwait(false);

            // Get requires the stream exists
            if (stream == null || stream.StreamVersion == -1)
                throw new NotFoundException($"Aggregate stream [{streamId}] in bucket [{bucket}] type {typeof(T).FullName} not found");

            // Call the 'private' constructor
            root = Newup(stream, _builder);

            if (snapshot != null && root is ISnapshotting)
            {
                Logger.Write(LogLevel.Debug, () => $"Restoring snapshot version {snapshot.Version} to stream id [{streamId}] bucket [{bucket}] version {stream.StreamVersion}");
                ((ISnapshotting)root).RestoreSnapshot(snapshot.Payload);
            }

            (root as IEventSource).Hydrate(stream.Events.Select(e => e.Event));

            return root;
        }

        public virtual Task<T> New<TId>(TId id)
        {
            return New<TId>(Defaults.Bucket, id);
        }

        public async Task<T> New<TId>(String bucket, TId id)
        {
            var root = await New(bucket, id.ToString()).ConfigureAwait(false);
            (root as IEventSource<TId>).Id = id;

            return root;
        }
        public async Task<T> New(String bucket, String streamId)
        {
            Logger.Write(LogLevel.Debug, () => $"Creating new stream id [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName} in store");
            var stream = await _store.NewStream<T>(bucket, streamId).ConfigureAwait(false);
            var root = Newup(stream, _builder);

            var cacheId = String.Format("{0}.{1}", bucket, streamId);
            _tracked[cacheId] = root;
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


    }
}