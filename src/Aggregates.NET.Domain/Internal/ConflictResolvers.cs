using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates.Internal
{

    delegate IResolveConflicts ResolverBuilder(IBuilder builder, Type type);

    class ConcurrencyStrategy : Enumeration<ConcurrencyStrategy, ConcurrencyConflict>
    {
        public static ConcurrencyStrategy Ignore = new ConcurrencyStrategy(ConcurrencyConflict.Ignore, "Ignore",
            (b, _) =>
            {
                var settings = b.Build<ReadOnlySettings>();
                return new IgnoreConflictResolver(b.Build<IStoreEvents>(),
                    settings.Get<StreamIdGenerator>("StreamGenerator"));
            });
        public static ConcurrencyStrategy Discard = new ConcurrencyStrategy(ConcurrencyConflict.Discard, "Discard", (b, _) => b.Build<DiscardConflictResolver>());
        public static ConcurrencyStrategy ResolveStrongly = new ConcurrencyStrategy(ConcurrencyConflict.ResolveStrongly, "ResolveStrongly", (b, _) => b.Build<ResolveStronglyConflictResolver>());
        public static ConcurrencyStrategy ResolveWeakly = new ConcurrencyStrategy(ConcurrencyConflict.ResolveWeakly, "ResolveWeakly", (b, _) => b.Build<ResolveWeaklyConflictResolver>());
        public static ConcurrencyStrategy Custom = new ConcurrencyStrategy(ConcurrencyConflict.Custom, "Custom", (b, type) => (IResolveConflicts)b.Build(type));

        public ConcurrencyStrategy(ConcurrencyConflict value, string displayName, ResolverBuilder builder) : base(value, displayName)
        {
            Build = builder;
        }

        public ResolverBuilder Build { get; private set; }
    }

    /// <summary>
    /// Conflict from the store is ignored, events will always be written
    /// </summary>
    internal class IgnoreConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger("IgnoreConflictResolver");

        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _streamGen;

        public IgnoreConflictResolver(IStoreEvents store, StreamIdGenerator streamGen)
        {
            _store = store;
            _streamGen = streamGen;
        }

        public async Task Resolve<T>(T entity, IEnumerable<IWritableEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var stream = entity.Stream;
            Logger.Write(LogLevel.Info, () => $"Resolving {uncommitted.Count()} uncommitted events to stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");

            foreach (var u in uncommitted)
            {
                entity.Apply(u.Event as IEvent, metadata: new Dictionary<string,string> { {"ConflictResolution", ConcurrencyConflict.Ignore.ToString()} });
            }

            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId);
            await _store.WriteEvents(streamName, uncommitted, commitHeaders).ConfigureAwait(false);
            stream.Flush(true);
        }
    }
    /// <summary>
    /// Conflicted events are discarded
    /// </summary>
    internal class DiscardConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger("DiscardConflictResolver");

        public Task Resolve<T>(T entity, IEnumerable<IWritableEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var stream = entity.Stream;
            Logger.Write(LogLevel.Info, () => $"Discarding {uncommitted.Count()} conflicting uncommitted events to stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");

            return Task.CompletedTask;
        }
    }
    /// <summary>
    /// Pull latest events from store, merge into stream and re-commit
    /// </summary>
    internal class ResolveStronglyConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger("ResolveStronglyConflictResolver");

        private readonly IStoreStreams _store;

        public ResolveStronglyConflictResolver(IStoreStreams eventstore)
        {
            _store = eventstore;
        }

        public async Task Resolve<T>(T entity, IEnumerable<IWritableEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var stream = entity.Stream;
            Logger.Write(LogLevel.Info, () => $"Resolving {uncommitted.Count()} uncommitted events to stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");

            try
            {
                await _store.Freeze<T>(stream.Bucket, stream.StreamId).ConfigureAwait(false);

                var latestEvents =
                    await
                        _store.GetEvents<T>(stream.Bucket, stream.StreamId, stream.CommitVersion + 1)
                            .ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Stream is {latestEvents.Count()} events behind store");

                var writableEvents = latestEvents as IWritableEvent[] ?? latestEvents.ToArray();
                stream.Concat(writableEvents);
                entity.Hydrate(writableEvents.Select(x => x.Event as IEvent));


                Logger.Write(LogLevel.Debug, () => "Merging conflicted events");
                try
                {
                    foreach (var u in uncommitted)
                        entity.Conflict(u.Event as IEvent, metadata: new Dictionary<string, string> { { "ConflictResolution", ConcurrencyConflict.ResolveStrongly.ToString() }});
                }
                catch (NoRouteException e)
                {
                    Logger.Write(LogLevel.Info, () => $"Failed to resolve conflict: {e.Message}");
                    throw new ConflictResolutionFailedException("Failed to resolve conflict", e);
                }

                Logger.Write(LogLevel.Debug, () => "Successfully merged conflicted events");

                if (stream.StreamVersion != stream.CommitVersion && entity is ISnapshotting &&
                    ((ISnapshotting) entity).ShouldTakeSnapshot())
                {
                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Taking snapshot of {typeof(T).FullName} id [{entity.StreamId}] version {stream.StreamVersion}");
                    var memento = ((ISnapshotting) entity).TakeSnapshot();
                    stream.AddSnapshot(memento);
                }

                await stream.Commit(commitId, commitHeaders).ConfigureAwait(false);
            }
            finally
            {
                await _store.Unfreeze<T>(stream.Bucket, stream.StreamId).ConfigureAwait(false);
            }
        }
    }
    /// <summary>
    /// Save conflicts for later processing, can only be used if the stream can never fail to merge
    /// </summary>
    internal class ResolveWeaklyConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger("ResolveWeaklyConflictResolver");

        private readonly IStoreStreams _store;
        private readonly IDelayedChannel _delay;

        public ResolveWeaklyConflictResolver(IStoreStreams eventstore, IDelayedChannel delay)
        {
            _store = eventstore;
            _delay = delay;
        }

        public async Task Resolve<T>(T entity, IEnumerable<IWritableEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            // Store conflicting events in memory
            // After 100 or so pile up pull the latest stream and attempt to write them again

            foreach (var @event in uncommitted)
                await _delay.AddToQueue(entity.StreamId, @event).ConfigureAwait(false);

            // Todo: make 30 seconds configurable
            var age = await _delay.Age(entity.StreamId).ConfigureAwait(false);
            if (!age.HasValue || age < TimeSpan.FromSeconds(30))
                return;

            var stream = entity.Stream;
            Logger.Write(LogLevel.Debug, () => $"Starting weak conflict resolve for stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");
            try
            {
                try
                {
                    await _store.Freeze<T>(stream.Bucket, stream.StreamId).ConfigureAwait(false);
                }
                catch (VersionException)
                {
                    Logger.Write(LogLevel.Debug, () => $"Stopping weak conflict resolve - someone else is processing");
                    return;
                }
                uncommitted = (await _delay.Pull(entity.StreamId).ConfigureAwait(false)).Cast<IWritableEvent>();
                // If someone else pulled while we were waiting
                if (!uncommitted.Any())
                    return;
                Logger.Write(LogLevel.Info,
                    () =>
                            $"Resolving {uncommitted.Count()} uncommitted events to stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");

                var latestEvents =
                    await
                        _store.GetEvents<T>(stream.Bucket, stream.StreamId, stream.CommitVersion + 1)
                            .ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Stream is {latestEvents.Count()} events behind store");

                var writableEvents = latestEvents as IWritableEvent[] ?? latestEvents.ToArray();
                stream.Concat(writableEvents);
                entity.Hydrate(writableEvents.Select(x => x.Event as IEvent));


                Logger.Write(LogLevel.Debug, () => "Merging conflicted events");
                try
                {
                    foreach (var u in uncommitted)
                        entity.Conflict(u.Event as IEvent, metadata: new Dictionary<string, string> { { "ConflictResolution", ConcurrencyConflict.ResolveWeakly.ToString() }});
                }
                catch (NoRouteException e)
                {
                    Logger.Write(LogLevel.Info, () => $"Failed to resolve conflict: {e.Message}");
                    throw new ConflictResolutionFailedException("Failed to resolve conflict", e);
                }


                Logger.Write(LogLevel.Debug, () => "Successfully merged conflicted events");

                if (stream.StreamVersion != stream.CommitVersion && entity is ISnapshotting &&
                    ((ISnapshotting) entity).ShouldTakeSnapshot())
                {
                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Taking snapshot of [{typeof(T).FullName}] id [{entity.StreamId}] version {stream.StreamVersion}");
                    var memento = ((ISnapshotting) entity).TakeSnapshot();
                    stream.AddSnapshot(memento);
                }

                await stream.Commit(commitId, commitHeaders).ConfigureAwait(false);
            }
            finally
            {
                await _store.Unfreeze<T>(stream.Bucket, stream.StreamId).ConfigureAwait(false);
            }

        }

    }

}
