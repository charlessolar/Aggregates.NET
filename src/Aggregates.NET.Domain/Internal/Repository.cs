using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Attributes;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using AggregateException = Aggregates.Exceptions.AggregateException;

namespace Aggregates.Internal
{
    // Todo: The hoops we jump through to support <TId> can be simplified by just using an Id class with implicit converters from string, int, guid, etc.

    class Repository<T> : IRepository<T> where T : class, IEventSource
    {
        private static OptimisticConcurrencyAttribute _conflictResolution;

        private static readonly ILog Logger = LogManager.GetLogger(typeof(Repository<>));
        private readonly IStoreStreams _store;
        private readonly IStoreSnapshots _snapstore;
        private readonly IBuilder _builder;
        private readonly ReadOnlySettings _settings;

        private static readonly Histogram WrittenEvents = Metric.Histogram("Written Events", Unit.Events);
        private static readonly Meter Conflicts = Metric.Meter("Conflicts", Unit.Items);
        private static readonly Meter ConflictsResolved = Metric.Meter("Conflicts Resolved", Unit.Items);
        private static readonly Meter ConflictsUnresolved = Metric.Meter("Conflicts Unresolved", Unit.Items);
        private static readonly Meter WriteErrors = Metric.Meter("Event Write Errors", Unit.Errors);
        private static readonly Metrics.Timer CommitTime = Metric.Timer("Repository Commit Time", Unit.Items);
        private static readonly Metrics.Timer ConflictResolutionTime = Metric.Timer("Conflict Resolution Time", Unit.Items);

        protected readonly IDictionary<string, T> Tracked = new Dictionary<string, T>();

        private bool _disposed;

        public Repository(IBuilder builder)
        {
            _builder = builder;
            _snapstore = _builder.Build<IStoreSnapshots>();
            _store = _builder.Build<IStoreStreams>();
            _settings = _builder.Build<ReadOnlySettings>();
            _store.Builder = _builder;

            // Conflict resolution is strong by default
            if (_conflictResolution == null)
                _conflictResolution = (OptimisticConcurrencyAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(OptimisticConcurrencyAttribute))
                    ?? new OptimisticConcurrencyAttribute(ConcurrencyConflict.ResolveStrongly);
        }

        async Task<Guid> IRepository.Commit(Guid commitId, Guid startingEventId, IDictionary<string, string> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(T).FullName} starting commit {commitId}");
            var written = 0;
            using (CommitTime.NewContext())
            {
                foreach (var tracked in Tracked.Values)
                {
                    // Instead of skipping streams with no new events, "commit" them to verify they are the same version as we read
                    //if (tracked.Stream.TotalUncommitted == 0) return startingEventId;

                    var headers = new Dictionary<string, string>(commitHeaders);

                    var stream = tracked.Stream;

                    Interlocked.Add(ref written, stream.TotalUncommitted);

                    if (stream.StreamVersion != stream.CommitVersion && tracked is ISnapshotting &&
                        (tracked as ISnapshotting).ShouldTakeSnapshot())
                    {
                        Logger.Write(LogLevel.Debug,
                            () =>
                                    $"Taking snapshot of [{tracked.GetType().FullName}] id [{tracked.StreamId}] version {tracked.Version}");
                        var memento = (tracked as ISnapshotting).TakeSnapshot();
                        stream.AddSnapshot(memento, headers);
                    }
                    
                    try
                    {
                        startingEventId = await stream.Commit(commitId, startingEventId, headers).ConfigureAwait(false);
                    }
                    catch (VersionException e)
                    {
                        Conflicts.Mark();
                        // If we expected no stream, no reason to try to resolve the conflict
                        if (stream.CommitVersion == -1)
                        {
                            Logger.Warn(
                                $"New stream [{tracked.StreamId}] entity {tracked.GetType().FullName} already exists in store");
                            throw new ConflictResolutionFailedException(
                                $"New stream [{tracked.StreamId}] entity {tracked.GetType().FullName} already exists in store");
                        }

                        await _store.Evict<T>(stream.Bucket, stream.StreamId).ConfigureAwait(false);
                        await _snapstore.Evict<T>(stream.Bucket, stream.StreamId).ConfigureAwait(false);
                        try
                        {
                            Logger.Write(LogLevel.Debug,
                                () =>
                                        $"Stream [{tracked.StreamId}] entity {tracked.GetType().FullName} version {stream.StreamVersion} has version conflicts with store - Message: {e.Message}");
                            // make new clean entity

                            Logger.Write(LogLevel.Debug,
                                () => $"Attempting to resolve conflict with strategy {_conflictResolution.Conflict}");

                            using (ConflictResolutionTime.NewContext())
                            {
                                var uncommitted = stream.Uncommitted.ToList();
                                stream.Flush(false);
                                var clean = await GetUntracked(stream).ConfigureAwait(false);

                                try
                                {
                                    var strategy = _conflictResolution.Conflict.Build(_builder,
                                        _conflictResolution.Resolver);
                                    startingEventId =
                                        await
                                            strategy.Resolve(clean, uncommitted, commitId, startingEventId,
                                                commitHeaders).ConfigureAwait(false);

                                }
                                catch (ConflictingCommandException)
                                {
                                    throw new ConflictResolutionFailedException("Failed to resolve stream conflict");
                                }
                            }

                            Logger.WriteFormat(LogLevel.Debug,
                                "Stream [{0}] entity {1} version {2} had version conflicts with store - successfully resolved",
                                tracked.StreamId, tracked.GetType().FullName, stream.StreamVersion);
                            ConflictsResolved.Mark();
                        }
                        catch (AbandonConflictException abandon)
                        {
                            ConflictsUnresolved.Mark();
                            Logger.WriteFormat(LogLevel.Error,
                                "Stream [{0}] entity {1} has version conflicts with store - abandoning resolution",
                                tracked.StreamId, tracked.GetType().FullName);
                            throw new ConflictResolutionFailedException(
                                $"Aborted conflict resolution for stream [{tracked.StreamId}] entity {tracked.GetType().FullName}",
                                abandon);
                        }
                        catch (Exception ex)
                        {
                            ConflictsUnresolved.Mark();
                            Logger.WriteFormat(LogLevel.Error,
                                "Stream [{0}] entity {1} has version conflicts with store - FAILED to resolve due to: {3}: {2}",
                                tracked.StreamId, tracked.GetType().FullName, ex.Message, ex.GetType().Name);
                            throw new ConflictResolutionFailedException(
                                $"Failed to resolve conflict for stream [{tracked.StreamId}] entity {tracked.GetType().FullName} due to exception",
                                ex);
                        }

                    }
                    catch (PersistenceException e)
                    {
                        Logger.WriteFormat(LogLevel.Warn,
                            "Failed to commit events to store for stream: [{0}] bucket [{1}] Exception: {3}: {2}",
                            stream.StreamId, stream.Bucket, e.Message, e.GetType().Name);
                        WriteErrors.Mark();
                        throw;
                    }
                    catch (DuplicateCommitException)
                    {
                        Logger.WriteFormat(LogLevel.Warn,
                            "Detected a double commit for stream: [{0}] bucket [{1}] - discarding changes for this stream",
                            stream.StreamId, stream.Bucket);
                        WriteErrors.Mark();
                        // I was throwing this, but if this happens it means the events for this message have already been committed.  Possibly as a partial message failure earlier. 
                        // Im changing to just discard the changes, perhaps can take a deeper look later if this ever bites me on the ass
                        //throw;
                    }


                }
            }
            WrittenEvents.Update(written);
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(T).FullName} finished commit {commitId}");
            return startingEventId;
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

            Tracked.Clear();

            _disposed = true;
        }

        public virtual Task<T> TryGet<TId>(TId id)
        {
            return TryGet(Defaults.Bucket, id);
        }
        public async Task<T> TryGet<TId>(string bucket, TId id)
        {
            if (id == null) return null;
            if (typeof(TId) == typeof(string) && string.IsNullOrEmpty(id as string)) return null;
            try
            {
                return await Get(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return null;
        }

        public virtual Task<T> Get<TId>(TId id)
        {
            return Get(Defaults.Bucket, id);
        }

        public async Task<T> Get<TId>(string bucket, TId id)
        {
            Logger.Write(LogLevel.Debug, () => $"Retreiving aggregate id [{id}] in bucket [{bucket}] for type {typeof(T).FullName} in store");
            var root = await Get(bucket, id.ToString()).ConfigureAwait(false);
            (root as IEventSource<TId>).Id = id;
            return root;
        }
        public async Task<T> Get(string bucket, string id)
        {
            var cacheId = $"{bucket}.{id}";
            T root;
            if (!Tracked.TryGetValue(cacheId, out root))
                Tracked[cacheId] = root = await GetUntracked(bucket, id).ConfigureAwait(false);

            return root;
        }
        private async Task<T> GetUntracked(string bucket, string streamId)
        {
            ISnapshot snapshot = null;
            if(typeof(ISnapshotting).IsAssignableFrom(typeof(T)))
                snapshot = await _snapstore.GetSnapshot<T>(bucket, streamId).ConfigureAwait(false);

            var stream = await _store.GetStream<T>(bucket, streamId, snapshot).ConfigureAwait(false);

            // Get requires the stream exists
            if (stream == null || stream.StreamVersion == -1)
                throw new NotFoundException($"Aggregate stream [{streamId}] in bucket [{bucket}] type {typeof(T).FullName} not found");

            return await GetUntracked(stream).ConfigureAwait(false);
        }
        private Task<T> GetUntracked(IEventStream stream)
        {
            // Call the 'private' constructor
            var root = Newup(stream, _builder);

            if (stream.CurrentMemento != null)
                (root as ISnapshotting)?.RestoreSnapshot(stream.CurrentMemento);

            root.Hydrate(stream.Committed.Select(e => e.Event as IEvent));

            return Task.FromResult(root);
        }

        public virtual Task<T> New<TId>(TId id)
        {
            return New(Defaults.Bucket, id);
        }

        public async Task<T> New<TId>(string bucket, TId id)
        {
            var root = await New(bucket, id.ToString()).ConfigureAwait(false);
            (root as IEventSource<TId>).Id = id;

            return root;
        }
        public async Task<T> New(string bucket, string streamId)
        {
            Logger.Write(LogLevel.Debug, () => $"Creating new stream id [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName} in store");
            var stream = await _store.NewStream<T>(bucket, streamId).ConfigureAwait(false);
            var root = Newup(stream, _builder);

            var cacheId = $"{bucket}.{streamId}";
            Tracked[cacheId] = root;
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

            return root;
        }


    }
}