using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public static ConcurrencyStrategy Throw = new ConcurrencyStrategy(ConcurrencyConflict.Throw, "Throw", (b, _) => b.Build<ThrowConflictResolver>());
        public static ConcurrencyStrategy Ignore = new ConcurrencyStrategy(ConcurrencyConflict.Ignore, "Ignore",
            (b, _) =>
            {
                var settings = b.Build<ReadOnlySettings>();
                return new IgnoreConflictResolver(b.Build<IStoreEvents>(),
                    settings.Get<StreamIdGenerator>("StreamGenerator"));
            });
        public static ConcurrencyStrategy Discard = new ConcurrencyStrategy(ConcurrencyConflict.Discard, "Discard", (b, _) => b.Build<DiscardConflictResolver>());
        public static ConcurrencyStrategy ResolveStrongly = new ConcurrencyStrategy(ConcurrencyConflict.ResolveStrongly, "ResolveStrongly",
            (b, _) =>
            {
                var settings = b.Build<ReadOnlySettings>();
                return new ResolveStronglyConflictResolver(b.Build<IStoreStreams>(), b.Build<IStoreEvents>(), settings.Get<StreamIdGenerator>("StreamGenerator"));
            });
        public static ConcurrencyStrategy ResolveWeakly = new ConcurrencyStrategy(ConcurrencyConflict.ResolveWeakly, "ResolveWeakly",
            (b, _) =>
            {
                var settings = b.Build<ReadOnlySettings>();
                return new ResolveWeaklyConflictResolver(b.Build<IStoreStreams>(), b.Build<IStoreEvents>(), b.Build<IDelayedChannel>(), settings.Get<StreamIdGenerator>("StreamGenerator"));
            });
        public static ConcurrencyStrategy Custom = new ConcurrencyStrategy(ConcurrencyConflict.Custom, "Custom", (b, type) => (IResolveConflicts)b.Build(type));

        public ConcurrencyStrategy(ConcurrencyConflict value, string displayName, ResolverBuilder builder) : base(value, displayName)
        {
            Build = builder;
        }

        public ResolverBuilder Build { get; private set; }
    }

    internal class ThrowConflictResolver : IResolveConflicts
    {
        public Task Resolve<T>(T entity, IEnumerable<IFullEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            throw new ConflictResolutionFailedException("No conflict resolution attempted");
        }
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

        public async Task Resolve<T>(T entity, IEnumerable<IFullEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var sourced = (IEventSourced)entity;

            var stream = sourced.Stream;
            Logger.Write(LogLevel.Info, () => $"Resolving {uncommitted.Count()} uncommitted events to stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");

            foreach (var u in uncommitted)
            {
                sourced.Apply(u.Event as IEvent, metadata: new Dictionary<string, string> { { "ConflictResolution", ConcurrencyConflict.Ignore.ToString() } });
            }

            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId, stream.Parents);
            await _store.WriteEvents(streamName, uncommitted, commitHeaders).ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Conflicted events are discarded
    /// </summary>
    internal class DiscardConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger("DiscardConflictResolver");

        public Task Resolve<T>(T entity, IEnumerable<IFullEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var sourced = (IEventSourced)entity;
            var stream = sourced.Stream;
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
        private readonly IStoreEvents _eventstore;
        private readonly StreamIdGenerator _streamGen;

        public ResolveStronglyConflictResolver(IStoreStreams store, IStoreEvents eventstore, StreamIdGenerator streamGen)
        {
            _store = store;
            _eventstore = eventstore;
            _streamGen = streamGen;
        }

        public async Task Resolve<T>(T entity, IEnumerable<IFullEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var sourced = (IEventSourced)entity;

            var stream = sourced.Stream;
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId, stream.Parents);
            Logger.Write(LogLevel.Info, () => $"Resolving {uncommitted.Count()} uncommitted events to stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");

            try
            {
                // Only need to freeze if resolving takes "long"
                if(uncommitted.Count() > 50)
                    await _store.Freeze<T>(stream).ConfigureAwait(false);
                
                var latestEvents =
                    await _eventstore.GetEvents(streamName, stream.CommitVersion + 1).ConfigureAwait(false);
                Logger.Write(LogLevel.Info, () => $"Stream is {latestEvents.Count()} events behind store");
                
                sourced.Hydrate(latestEvents.Select(x => x.Event as IEvent));

                Logger.Write(LogLevel.Debug, () => "Merging conflicted events");
                try
                {
                    foreach (var u in uncommitted)
                    {
                        if(u.Descriptor.StreamType== StreamTypes.Domain)
                            sourced.Conflict(u.Event as IEvent,
                                metadata: new Dictionary<string, string>
                                {
                                    {"ConflictResolution", ConcurrencyConflict.ResolveStrongly.ToString()}
                                });
                        else if(u.Descriptor.StreamType == StreamTypes.OOB)
                            sourced.Raise(u.Event as IEvent, u.Descriptor.Headers[Defaults.OobHeaderKey],
                                metadata: new Dictionary<string, string>
                                {
                                    {"ConflictResolution", ConcurrencyConflict.ResolveStrongly.ToString()}
                                });
                    }
                }
                catch (NoRouteException e)
                {
                    Logger.Write(LogLevel.Info, () => $"Failed to resolve conflict: {e.Message}");
                    throw new ConflictResolutionFailedException("Failed to resolve conflict", e);
                }

                Logger.Write(LogLevel.Debug, () => "Successfully merged conflicted events");

                if (stream.StreamVersion != stream.CommitVersion && entity is ISnapshotting &&
                    ((ISnapshotting)entity).ShouldTakeSnapshot())
                {
                    Logger.Write(LogLevel.Debug,
                        () => $"Taking snapshot of {typeof(T).FullName} id [{entity.Id}] version {stream.StreamVersion}");
                    var memento = ((ISnapshotting)entity).TakeSnapshot();
                    stream.AddSnapshot(memento);
                }

                await _store.WriteStream<T>(commitId, stream, commitHeaders).ConfigureAwait(false);
            }
            finally
            {
                if (uncommitted.Count() > 50)
                    await _store.Unfreeze<T>(stream).ConfigureAwait(false);
            }
        }
    }
        
    internal class ConflictingEvents : IMessage
    {
        public string EntityType { get; set; }
        public string Bucket { get; set; }
        public Id StreamId { get; set; }
        public IEnumerable<Tuple<string, Id>> Parents { get; set; }

        public IEnumerable<IFullEvent> Events { get; set; }
    }
    /// <summary>
    /// Save conflicts for later processing, can only be used if the stream can never fail to merge
    /// </summary>
    internal class ResolveWeaklyConflictResolver :
        IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger("ResolveWeaklyConflictResolver");

        private readonly IStoreStreams _store;
        private readonly IStoreEvents _eventstore;
        private readonly IDelayedChannel _delay;
        private readonly StreamIdGenerator _streamGen;


        public ResolveWeaklyConflictResolver(IStoreStreams store, IStoreEvents eventstore, IDelayedChannel delay, StreamIdGenerator streamGen)
        {
            _store = store;
            _eventstore = eventstore;
            _delay = delay;
            _streamGen = streamGen;
        }

        private IEnumerable<Tuple<string, Id>> BuildParentList(IEventSource entity)
        {
            var results = new List<Tuple<string, Id>>();
            while (entity.Parent != null)
            { 
                results.Add( new Tuple<string,Id>(entity.Parent.GetType().AssemblyQualifiedName, entity.Parent.Id));
                entity = entity.Parent;
            }
            return results;
        }

        public async Task Resolve<T>(T entity, IEnumerable<IFullEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var sourced = (IEventSourced)entity;
            // Store conflicting events in memory
            // After 100 or so pile up pull the latest stream and attempt to write them again

            var streamName = _streamGen(typeof(T), StreamTypes.Domain, sourced.Stream.Bucket, sourced.Stream.StreamId, sourced.Stream.Parents);

            var message = new ConflictingEvents
            {
                Bucket = sourced.Stream.Bucket,
                StreamId = sourced.Stream.StreamId,
                EntityType = typeof(T).AssemblyQualifiedName,
                Parents = BuildParentList(entity),
                Events = uncommitted
            };

            var package = new DelayedMessage
            {
                MessageId = Guid.NewGuid().ToString(),
                Headers = new Dictionary<string, string>(),
                Message = message,
                Received = DateTime.UtcNow,
                ChannelKey = streamName
            };
            foreach (var header in commitHeaders)
                package.Headers[$"Conflict.{header.Key}"] = header.Value;

            await _delay.AddToQueue(ConcurrencyConflict.ResolveWeakly.ToString(), package, streamName)
                    .ConfigureAwait(false);

            // Todo: make 30 seconds configurable
            var age = await _delay.Age(ConcurrencyConflict.ResolveWeakly.ToString(), streamName).ConfigureAwait(false);
            if (!age.HasValue || age < TimeSpan.FromSeconds(30))
                return;

            var stream = sourced.Stream;
            Logger.Write(LogLevel.Info,
                () => $"Starting weak conflict resolve for stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");
            try
            {
                await _store.Freeze<T>(stream).ConfigureAwait(false);

                var delayed = await _delay.Pull(streamName, max: 200).ConfigureAwait(false);

                // If someone else pulled while we were waiting
                if (!delayed.Any())
                    return;

                Logger.Write(LogLevel.Info,
                    () => $"Resolving {delayed.Count()} uncommitted events to stream [{stream.StreamId}] type [{typeof(T).FullName}] bucket [{stream.Bucket}]");

                var latestEvents =
                    await _eventstore.GetEvents(streamName, stream.CommitVersion + 1L)
                        .ConfigureAwait(false);
                Logger.Write(LogLevel.Info,
                    () => $"Stream [{stream.StreamId}] bucket [{stream.Bucket}] is {latestEvents.Count()} events behind store");
                
                sourced.Hydrate(latestEvents.Select(x => x.Event as IEvent));


                Logger.Write(LogLevel.Debug, () => $"Merging {delayed.Count()} conflicted events");
                try
                {
                    foreach (var u in delayed.Select(x => x.Message as ConflictingEvents).SelectMany(x => x.Events))
                    {
                        if (u.Descriptor.StreamType == StreamTypes.Domain)
                            sourced.Conflict(u.Event as IEvent,
                                metadata: new Dictionary<string, string>
                                {
                                    {"ConflictResolution", ConcurrencyConflict.ResolveStrongly.ToString()}
                                });
                        else if (u.Descriptor.StreamType == StreamTypes.OOB)
                            sourced.Raise(u.Event as IEvent, u.Descriptor.Headers[Defaults.OobHeaderKey],
                                metadata: new Dictionary<string, string>
                                {
                                    {"ConflictResolution", ConcurrencyConflict.ResolveStrongly.ToString()}
                                });
                    }
                }
                catch (NoRouteException e)
                {
                    Logger.Write(LogLevel.Info, () => $"Failed to resolve conflict: {e.Message}");
                    throw new ConflictResolutionFailedException("Failed to resolve conflict", e);
                }

                Logger.Write(LogLevel.Info, () => "Successfully merged conflicted events");

                if (stream.StreamVersion != stream.CommitVersion && entity is ISnapshotting &&
                    ((ISnapshotting) entity).ShouldTakeSnapshot())
                {
                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Taking snapshot of [{typeof(T).FullName}] id [{entity.Id}] version {stream.StreamVersion}");
                    var memento = ((ISnapshotting) entity).TakeSnapshot();
                    stream.AddSnapshot(memento);
                }

                await _store.WriteStream<T>(commitId, stream, commitHeaders).ConfigureAwait(false);
            }
            finally
            {
                await _store.Unfreeze<T>(stream).ConfigureAwait(false);
            }
        }
    }
    /// <summary>
    /// When the above pushes a conflicting event package onto the delayed queue it can end up 
    /// flushed out to the store.  In that case the conflict will be handled via IHandleMessages
    /// The methods here are mostly a hack to rebuild the entity and its parents based on 
    /// information from the conflicting events package.
    /// </summary>
    internal class HandleConflictingEvents :
        IHandleMessages<ConflictingEvents>
    {
        internal static readonly ILog Logger = LogManager.GetLogger("HandleConflictingEvents");
        private readonly IUnitOfWork _uow;

        public HandleConflictingEvents(IUnitOfWork uow)
        {
            _uow = uow;
        }

        private async Task<IEventSourced> GetBase(string bucket, string type, Id id, IEventSourced parent = null)
        {
            var entityType = Type.GetType(type, false, true);
            if (entityType == null)
            {
                Logger.Error($"Received conflicting events message for unknown type {type}");
                throw new ArgumentException($"Received conflicting events message for unknown type {type}");
            }

            // We have the type name, hack the generic parameters to build Entity
            if (parent == null)
            {
                var method = typeof(IUnitOfWork).GetMethod("For", new Type[] {}).MakeGenericMethod(entityType);

                var repo = method.Invoke(_uow, new object[] { });
                
                method = repo.GetType().GetMethod("Get", new Type[] {typeof(string), typeof(Id)});
                // Note: we can't just cast method.Invoke into Task<IEventSourced> because Task is not covariant
                // believe it or not this is the easiest solution
                var task = (Task)method.Invoke(repo, new object[] {bucket, id});
                await task.ConfigureAwait(false);
                return task.GetType().GetProperty("Result").GetValue(task) as IEventSourced;
            }
            else
            {
                var method =
                    typeof(IUnitOfWork).GetMethods()
                        .Single(x => x.Name=="For" && x.GetParameters().Length == 1)
                        .MakeGenericMethod(parent.GetType(), entityType);
                
                var repo = method.Invoke(_uow, new object[] { parent });

                method = repo.GetType().GetMethod("Get", new Type[] { typeof(Id) });
                var task = (Task)method.Invoke(repo, new object[] { id });
                await task.ConfigureAwait(false);
                return task.GetType().GetProperty("Result").GetValue(task) as IEventSourced;
            }
        }

        public async Task Handle(ConflictingEvents conflicts, IMessageHandlerContext ctx)
        {
            // Hydrate the entity, include all his parents
            IEventSourced parentBase = null;
            foreach (var parent in conflicts.Parents)
                parentBase = await GetBase(conflicts.Bucket, parent.Item1, parent.Item2, parentBase).ConfigureAwait(false);

            var target = await GetBase(conflicts.Bucket, conflicts.EntityType, conflicts.StreamId, parentBase).ConfigureAwait(false);
            var stream = target.Stream;

            Logger.Write(LogLevel.Info,
                () => $"Weakly resolving {conflicts.Events.Count()} conflicts on stream [{conflicts.StreamId}] type [{target.GetType().FullName}] bucket [{target.Stream.Bucket}]");

            // No need to pull from the delayed channel or hydrate as below because this is called from the Delayed system which means
            // the conflict is not in delayed cache and GetBase above pulls the latest stream

                Logger.Write(LogLevel.Debug, () => $"Merging {conflicts.Events.Count()} conflicted events");
            try
            {
                foreach (var u in conflicts.Events)
                    target.Conflict(u.Event as IEvent,
                        metadata:
                        new Dictionary<string, string>
                        {
                                {"ConflictResolution", ConcurrencyConflict.ResolveWeakly.ToString()}
                        });
            }
            catch (NoRouteException e)
            {
                Logger.Write(LogLevel.Info, () => $"Failed to resolve conflict: {e.Message}");
                throw new ConflictResolutionFailedException("Failed to resolve conflict", e);
            }

            Logger.Write(LogLevel.Info, () => "Successfully merged conflicted events");

            if (stream.StreamVersion != stream.CommitVersion && target is ISnapshotting &&
                ((ISnapshotting)target).ShouldTakeSnapshot())
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Taking snapshot of [{target.GetType().FullName}] id [{target.Id}] version {stream.StreamVersion}");
                var memento = ((ISnapshotting)target).TakeSnapshot();
                stream.AddSnapshot(memento);
            }
                // Dont call stream.Commit we are inside a UOW in this method it will do the commit for us
            
        }


    }

}
