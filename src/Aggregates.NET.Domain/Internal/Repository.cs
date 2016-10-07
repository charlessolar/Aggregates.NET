using Aggregates.Attributes;
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
        private static OptimisticConcurrencyAttribute ConflictResolution = null;

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

            // Conflict resolution is strong by default
            if (ConflictResolution == null)
                ConflictResolution = (OptimisticConcurrencyAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(OptimisticConcurrencyAttribute))
                    ?? new OptimisticConcurrencyAttribute(ConcurrencyConflict.ResolveStrongly);
        }

        async Task<Guid> IRepository.Commit(Guid commitId, Guid startingEventId, IDictionary<String, String> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(T).FullName} starting commit {commitId}");
            var written = 0;
            foreach (var tracked in _tracked.Values)
            {
                if (tracked.Stream.TotalUncommitted == 0) return startingEventId;

                var headers = new Dictionary<String, String>(commitHeaders);

                var stream = tracked.Stream;

                Interlocked.Add(ref written, stream.TotalUncommitted);

                if (stream.StreamVersion != stream.CommitVersion && tracked is ISnapshotting && (tracked as ISnapshotting).ShouldTakeSnapshot())
                {
                    Logger.Write(LogLevel.Debug, () => $"Taking snapshot of {tracked.GetType().FullName} id [{tracked.StreamId}] version {tracked.Version}");
                    var memento = (tracked as ISnapshotting).TakeSnapshot();
                    stream.AddSnapshot(memento, headers);
                }

                var evict = true;
                try
                {
                    startingEventId = await stream.Commit(commitId, startingEventId, headers).ConfigureAwait(false);
                    await _store.Cache<T>(stream);
                    evict = false;
                }
                catch (VersionException e)
                {
                    // If we expected no stream, no reason to try to resolve the conflict
                    if (stream.CommitVersion == -1)
                        throw new ConflictResolutionFailedException($"New stream [{tracked.StreamId}] entity {tracked.GetType().FullName} already exists in store");

                    WriteErrors.Mark();
                    Conflicts.Mark();
                    try
                    {
                        Logger.Write(LogLevel.Debug, () => $"Stream [{tracked.StreamId}] entity {tracked.GetType().FullName} version {stream.StreamVersion} has version conflicts with store - Message: {e.Message}");
                        // make new clean entity

                        Logger.Write(LogLevel.Debug, () => $"Attempting to resolve conflict with strategy {ConflictResolution.Conflict}");

                        var clean = await GetUntracked(tracked.Stream);

                        var tries = ConflictResolution.ResolveRetries ?? _settings.Get<Int32>("MaxConflictResolves");
                        var success = false;
                        do
                        {
                            try
                            {
                                switch (ConflictResolution.Conflict)
                                {
                                    case ConcurrencyConflict.ResolveStrongly:
                                    {
                                            var strategy = _builder.Build<ResolveStronglyConflictResolver>();
                                            startingEventId = await strategy.Resolve<T>(clean, stream.Uncommitted, commitId, startingEventId, commitHeaders);
                                            break;
                                        }
                                    case ConcurrencyConflict.Ignore:
                                        {
                                            var strategy = _builder.Build<IgnoreConflictResolver>();
                                            startingEventId = await strategy.Resolve<T>(clean, stream.Uncommitted, commitId, startingEventId, commitHeaders);
                                            break;
                                        }
                                    case ConcurrencyConflict.Discard:
                                        {
                                            var strategy = _builder.Build<DiscardConflictResolver>();
                                            startingEventId = await strategy.Resolve<T>(clean, stream.Uncommitted, commitId, startingEventId, commitHeaders);
                                            break;
                                        }
                                    case ConcurrencyConflict.ResolveWeakly:
                                        {
                                            var strategy = _builder.Build<ResolveWeaklyConflictResolver>();
                                            startingEventId = await strategy.Resolve<T>(clean, stream.Uncommitted, commitId, startingEventId, commitHeaders);
                                            break;
                                        }
                                    case ConcurrencyConflict.Custom:
                                        {
                                            var strategy = (IResolveConflicts)_builder.Build(ConflictResolution.Resolver);
                                            startingEventId = await strategy.Resolve<T>(clean, stream.Uncommitted, commitId, startingEventId, commitHeaders);
                                            break;
                                        }
                                }
                                success = true;
                            }
                            catch (ConflictingCommandException) { }
                        } while (!success && (--tries) > 0);
                        if (!success)
                        {
                            await _store.Evict<T>(stream.Bucket, stream.StreamId);
                            throw new ConflictResolutionFailedException("Failed to resolve stream conflict");
                        }
                        await _store.Cache<T>(clean.Stream);
                        evict = false;

                        Logger.WriteFormat(LogLevel.Debug, "Stream [{0}] entity {1} version {2} had version conflicts with store - successfully resolved", tracked.StreamId, tracked.GetType().FullName, stream.StreamVersion);
                        ConflictsResolved.Mark();
                    }
                    catch (AbandonConflictException abandon)
                    {
                        await _store.Evict<T>(stream.Bucket, stream.StreamId);
                        ConflictsUnresolved.Mark();
                        Logger.WriteFormat(LogLevel.Error, "Stream [{0}] entity {1} has version conflicts with store - abandoning resolution", tracked.StreamId, tracked.GetType().FullName);
                        throw new ConflictResolutionFailedException($"Aborted conflict resolution for stream [{tracked.StreamId}] entity {tracked.GetType().FullName}", abandon);
                    }
                    catch (Exception ex)
                    {
                        await _store.Evict<T>(stream.Bucket, stream.StreamId);
                        ConflictsUnresolved.Mark();
                        Logger.WriteFormat(LogLevel.Error, "Stream [{0}] entity {1} has version conflicts with store - FAILED to resolve due to: {3}: {2}", tracked.StreamId, tracked.GetType().FullName, ex.Message, ex.GetType().Name);
                        throw new ConflictResolutionFailedException($"Failed to resolve conflict for stream [{tracked.StreamId}] entity {tracked.GetType().FullName} due to exception", ex);
                    }

                }
                catch (PersistenceException e)
                {
                    Logger.WriteFormat(LogLevel.Warn, "Failed to commit events to store for stream: [{0}] bucket [{1}]\nException: {3}: {2}", stream.StreamId, stream.Bucket, e.Message, e.GetType().Name);
                    throw;
                }
                catch (DuplicateCommitException)
                {
                    Logger.WriteFormat(LogLevel.Warn, "Detected a double commit for stream: [{0}] bucket [{1}] - discarding changes for this stream", stream.StreamId, stream.Bucket);
                    // I was throwing this, but if this happens it means the events for this message have already been committed.  Possibly as a partial message failure earlier. 
                    // Im changing to just discard the changes, perhaps can take a deeper look later if this ever bites me on the ass
                    //throw;
                }
                finally
                {
                    if (evict)
                    {
                        await _store.Evict<T>(stream.Bucket, stream.StreamId);
                        WriteErrors.Mark();
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
            var snapshot = await _snapstore.GetSnapshot<T>(bucket, streamId).ConfigureAwait(false);
            var stream = await _store.GetStream<T>(bucket, streamId, snapshot).ConfigureAwait(false);

            // Get requires the stream exists
            if (stream == null || stream.StreamVersion == -1)
                throw new NotFoundException($"Aggregate stream [{streamId}] in bucket [{bucket}] type {typeof(T).FullName} not found");

            return await GetUntracked(stream);
        }
        private Task<T> GetUntracked(IEventStream stream)
        {
            T root;
            // Call the 'private' constructor
            root = Newup(stream, _builder);

            if (stream.CurrentMemento != null && root is ISnapshotting)
                ((ISnapshotting)root).RestoreSnapshot(stream.CurrentMemento);

            (root as IEventSource).Hydrate(stream.Events.Select(e => e.Event));

            return Task.FromResult(root);
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