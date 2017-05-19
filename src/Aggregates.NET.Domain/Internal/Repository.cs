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
    class Repository<TParent, T> : Repository<T>, IRepository<TParent, T> where TParent : Entity<TParent> where T : Entity<T, TParent>
    {
        private static readonly ILog Logger = LogManager.GetLogger("Repository");

        private readonly TParent _parent;

        public Repository(TParent parent, IBuilder builder) : base(builder)
        {
            _parent = parent;
        }

        public override async Task<T> TryGet(Id id)
        {
            if (id == null) return null;
            try
            {
                return await Get(id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return null;

        }
        public override async Task<T> Get(Id id)
        {
            var cacheId = $"{_parent.Stream.Bucket}.{_parent.BuildParentsString()}.{id}";
            T root;
            if (!Tracked.TryGetValue(cacheId, out root))
                Tracked[cacheId] = root = await GetUntracked(_parent.Stream.Bucket, id, _parent.BuildParents()).ConfigureAwait(false);

            root.Parent = _parent;

            return (T)root;
        }

        private async Task<T> GetUntracked(string bucket, Id streamId, IEnumerable<Id> parents)
        {
            parents = parents ?? new Id[] { };

            Logger.Write(LogLevel.Debug, () => $"Retreiving entity id [{streamId}] bucket [{bucket}] for type {typeof(T).FullName} in store");
            var stream = await _store.GetStream<T>(bucket, streamId, parents).ConfigureAwait(false);

            var entity = Newup(stream);
            entity.Parent = _parent;

            (entity as IEventSourced).Hydrate(stream.Committed.Select(e => e.Event as IEvent));

            Logger.Write(LogLevel.Debug, () => $"Hydrated aggregate id [{stream.StreamId}] in bucket [{stream.Bucket}] for type {typeof(T).FullName} to version {stream.CommitVersion}");
            return entity;
        }

        public override async Task<T> New(Id id)
        {
            var parent = (IEventSourced)_parent;

            Logger.Write(LogLevel.Debug, () => $"Creating new stream id [{id}] in bucket [{parent.Stream.Bucket}] for type {typeof(T).FullName} in store");
            var stream = await _store.NewStream<T>(parent.Stream.Bucket, id, parent.BuildParents()).ConfigureAwait(false);
            var root = Newup(stream);

            root.Parent = _parent;

            var cacheId = $"{parent.Stream.Bucket}.{parent.BuildParentsString()}.{id}";
            Tracked[cacheId] = root;
            return root;
        }
    }

    class Repository<T> : IRepository<T>, IRepository where T : Entity<T>
    {
        private static OptimisticConcurrencyAttribute _conflictResolution;

        private static readonly ILog Logger = LogManager.GetLogger("Repository");
        protected readonly IBuilder _builder;
        protected readonly IStoreStreams _store;

        private static readonly Meter WriteErrors = Metric.Meter("Event Write Errors", Unit.Errors);
        private static readonly Meter Conflicts = Metric.Meter("Conflicts", Unit.Items);
        private static readonly Meter ConflictsResolved = Metric.Meter("Conflicts Resolved", Unit.Items, tags: "debug");
        private static readonly Meter ConflictsUnresolved = Metric.Meter("Conflicts Unresolved", Unit.Items, tags: "debug");
        private static readonly Metrics.Timer CommitTime = Metric.Timer("Repository Commit Time", Unit.Items, tags: "debug");
        private static readonly Meter WrittenEvents = Metric.Meter("Repository Written Events", Unit.Events, tags: "debug");
        private static readonly Metrics.Timer ConflictResolutionTime = Metric.Timer("Conflict Resolution Time", Unit.Items, tags: "debug");

        protected readonly IDictionary<string, T> Tracked = new Dictionary<string, T>();

        private bool _disposed;

        public int TotalUncommitted => Tracked.Sum(x => x.Value.Stream.TotalUncommitted);
        public int ChangedStreams => Tracked.Count(x => x.Value.Stream.Dirty);

        public Repository(IBuilder builder)
        {
            _builder = builder;
            _store = builder.Build<IStoreStreams>();

            // Conflict resolution is strong by default
            if (_conflictResolution == null)
                _conflictResolution = (OptimisticConcurrencyAttribute)Attribute.GetCustomAttribute(typeof(T), typeof(OptimisticConcurrencyAttribute))
                    ?? new OptimisticConcurrencyAttribute(ConcurrencyConflict.Throw);
        }

        Task IRepository.Prepare(Guid commitId)
        {
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(T).FullName} starting prepare {commitId}");

            // Verify streams we read but didn't change are still save version
            return
                Tracked.Values
                    .Where(x => !x.Stream.Dirty)
                    .ToArray()
                    .WhenAllAsync((x) => _store.VerifyVersion<T>(x.Stream));
        }

        async Task IRepository.Commit(Guid commitId, IDictionary<string, string> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(T).FullName} starting commit {commitId}");
            var written = 0;
            using (CommitTime.NewContext())
            {
                await Tracked.Values
                    .ToArray()
                    .WhenAllAsync(async (tracked) =>
                    {
                        var headers = new Dictionary<string, string>(commitHeaders);

                        var stream = tracked.Stream;

                        Interlocked.Add(ref written, stream.TotalUncommitted);

                        if (stream.StreamVersion != stream.CommitVersion && tracked is ISnapshotting &&
                            (tracked as ISnapshotting).ShouldTakeSnapshot())
                        {
                            Logger.Write(LogLevel.Debug,
                                () => $"Taking snapshot of [{tracked.GetType().FullName}] id [{tracked.Id}] version {tracked.Version}");
                            var memento = (tracked as ISnapshotting).TakeSnapshot();
                            stream.AddSnapshot(memento);
                        }

                        try
                        {
                            await _store.WriteStream<T>(commitId, stream, headers).ConfigureAwait(false);
                        }
                        catch (VersionException e)
                        {
                            Logger.Write(LogLevel.Info,
                                       () => $"Stream [{tracked.Id}] entity {tracked.GetType().FullName} stream version {stream.StreamVersion} commit verison {stream.CommitVersion} has version conflicts with store - Message: {e.Message} Store: {e.InnerException?.Message}");

                            Conflicts.Mark();
                            // If we expected no stream, no reason to try to resolve the conflict
                            if (stream.CommitVersion == -1)
                            {
                                Logger.Warn(
                                    $"New stream [{tracked.Id}] entity {tracked.GetType().FullName} already exists in store");
                                throw new ConflictResolutionFailedException(
                                    $"New stream [{tracked.Id}] entity {tracked.GetType().FullName} already exists in store");
                            }

                            try
                            {
                                using (ConflictResolutionTime.NewContext())
                                {
                                    var uncommitted = stream.Uncommitted.ToList();
                                    // make new clean entity
                                    var clean = await GetUntracked(stream.Bucket, stream.StreamId, stream.Parents).ConfigureAwait(false);

                                    Logger.Write(LogLevel.Debug,
                                        () => $"Attempting to resolve conflict with strategy {_conflictResolution.Conflict}");
                                    var strategy = _conflictResolution.Conflict.Build(_builder, _conflictResolution.Resolver);
                                    await
                                        strategy.Resolve(clean, uncommitted, commitId,
                                            commitHeaders).ConfigureAwait(false);

                                }

                                Logger.WriteFormat(LogLevel.Info,
                                    "Stream [{0}] entity {1} version {2} had version conflicts with store - successfully resolved",
                                    tracked.Id, tracked.GetType().FullName, stream.StreamVersion);
                                ConflictsResolved.Mark();
                            }
                            catch (AbandonConflictException abandon)
                            {
                                ConflictsUnresolved.Mark();
                                Logger.WriteFormat(LogLevel.Error,
                                    "Stream [{0}] entity {1} has version conflicts with store - abandoning resolution",
                                    tracked.Id, tracked.GetType().FullName);
                                throw new ConflictResolutionFailedException(
                                    $"Aborted conflict resolution for stream [{tracked.Id}] entity {tracked.GetType().FullName}",
                                    abandon);
                            }
                            catch (Exception ex)
                            {
                                ConflictsUnresolved.Mark();
                                Logger.WriteFormat(LogLevel.Error,
                                    "Stream [{0}] entity {1} has version conflicts with store - FAILED to resolve due to: {3}: {2}",
                                    tracked.Id, tracked.GetType().FullName, ex.Message, ex.GetType().Name);
                                throw new ConflictResolutionFailedException(
                                    $"Failed to resolve conflict for stream [{tracked.Id}] entity {tracked.GetType().FullName} due to exception",
                                    ex);
                            }

                        }
                        catch (PersistenceException e)
                        {
                            Logger.WriteFormat(LogLevel.Warn,
                                "Failed to commit events to store for stream: [{0}] bucket [{1}] Exception: {3}: {2}",
                                stream.StreamId, stream.Bucket, e.Message, e.GetType().Name);
                            WriteErrors.Mark(e.Message);
                            throw;
                        }
                        catch (DuplicateCommitException)
                        {
                            Logger.WriteFormat(LogLevel.Warn,
                                "Detected a double commit for stream: [{0}] bucket [{1}] - discarding changes for this stream",
                                stream.StreamId, stream.Bucket);
                            WriteErrors.Mark("Duplicate");
                            // I was throwing this, but if this happens it means the events for this message have already been committed.  Possibly as a partial message failure earlier. 
                            // Im changing to just discard the changes, perhaps can take a deeper look later if this ever bites me on the ass
                            //throw;
                        }
                    });

            }
            WrittenEvents.Mark(typeof(T).FullName, written);
            Logger.Write(LogLevel.Debug, () => $"Repository {typeof(T).FullName} finished commit {commitId} wrote {written} events");

        }


        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            Tracked.Clear();

            _disposed = true;
        }

        public virtual Task<T> TryGet(Id id)
        {
            return TryGet(Defaults.Bucket, id);
        }
        public async Task<T> TryGet(string bucket, Id id)
        {
            if (id == null) return null;

            try
            {
                return await Get(bucket, id).ConfigureAwait(false);
            }
            catch (NotFoundException) { }
            return null;
        }

        public virtual Task<T> Get(Id id)
        {
            return Get(Defaults.Bucket, id);
        }

        public async Task<T> Get(string bucket, Id id)
        {
            var cacheId = $"{bucket}.{id}";
            T root;
            if (!Tracked.TryGetValue(cacheId, out root))
                Tracked[cacheId] = root = await GetUntracked(bucket, id).ConfigureAwait(false);

            return root;
        }
        private async Task<T> GetUntracked(string bucket, Id streamId, IEnumerable<Id> parents = null)
        {
            parents = parents ?? new Id[] { };

            Logger.Write(LogLevel.Debug, () => $"Retreiving entity id [{streamId}] bucket [{bucket}] for type {typeof(T).FullName} in store");
            var stream = await _store.GetStream<T>(bucket, streamId, parents).ConfigureAwait(false);

            var entity = Newup(stream);

            (entity as IEventSourced).Hydrate(stream.Committed.Select(e => e.Event as IEvent));

            Logger.Write(LogLevel.Debug, () => $"Hydrated aggregate id [{stream.StreamId}] in bucket [{stream.Bucket}] for type {typeof(T).FullName} to version {stream.CommitVersion}");
            return entity;
        }

        public virtual Task<T> New(Id id)
        {
            return New(Defaults.Bucket, id);
        }

        public async Task<T> New(string bucket, Id id)
        {
            Logger.Write(LogLevel.Debug, () => $"Creating new stream id [{id}] in bucket [{bucket}] for type {typeof(T).FullName} in store");
            var stream = await _store.NewStream<T>(bucket, id).ConfigureAwait(false);
            var root = Newup(stream);

            var cacheId = $"{bucket}.{id}";
            Tracked[cacheId] = root;
            return root;
        }

        protected T Newup(IEventStream stream)
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
                (root as INeedBuilder).Builder = _builder;
            if (root is INeedEventFactory)
                (root as INeedEventFactory).EventFactory = _builder.Build<IMessageCreator>();
            if (root is INeedRouteResolver)
                (root as INeedRouteResolver).Resolver = _builder.Build<IRouteResolver>();

            if (stream.Snapshot != null)
                (root as ISnapshotting)?.RestoreSnapshot(stream.Snapshot.Payload);

            return root;
        }


    }
}