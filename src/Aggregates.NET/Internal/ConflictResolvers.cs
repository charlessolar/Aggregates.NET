using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;
using Aggregates.Messages;

namespace Aggregates.Internal
{
    class ConcurrencyStrategy : Enumeration<ConcurrencyStrategy, ConcurrencyConflict>
    {
        public static ConcurrencyStrategy Throw = new ConcurrencyStrategy(ConcurrencyConflict.Throw, "Throw");
        public static ConcurrencyStrategy Ignore = new ConcurrencyStrategy(ConcurrencyConflict.Ignore, "Ignore");
        public static ConcurrencyStrategy Discard = new ConcurrencyStrategy(ConcurrencyConflict.Discard, "Discard");
        public static ConcurrencyStrategy ResolveStrongly = new ConcurrencyStrategy(ConcurrencyConflict.ResolveStrongly, "ResolveStrongly");
        public static ConcurrencyStrategy ResolveWeakly = new ConcurrencyStrategy(ConcurrencyConflict.ResolveWeakly, "ResolveWeakly");
        public static ConcurrencyStrategy Custom = new ConcurrencyStrategy(ConcurrencyConflict.Custom, "Custom");

        public ConcurrencyStrategy(ConcurrencyConflict value, string displayName) : base(value, displayName)
        {
        }

        public IResolveConflicts Build(Type type = null)
        {
            switch(this.Value)
            {
                case ConcurrencyConflict.Throw:
                    return Configuration.Settings.Container.Resolve<ThrowConflictResolver>();
                case ConcurrencyConflict.Ignore:
                    return Configuration.Settings.Container.Resolve<IgnoreConflictResolver>();
                case ConcurrencyConflict.Discard:
                    return Configuration.Settings.Container.Resolve<DiscardConflictResolver>();
                case ConcurrencyConflict.ResolveStrongly:
                    return Configuration.Settings.Container.Resolve<ResolveStronglyConflictResolver>();
                case ConcurrencyConflict.ResolveWeakly:
                    return Configuration.Settings.Container.Resolve<ResolveWeaklyConflictResolver>();
                case ConcurrencyConflict.Custom:
                    return (IResolveConflicts)Configuration.Settings.Container.Resolve(type);
            };
            throw new InvalidOperationException($"Unknown conflict resolver: {this.Value}");
        }
    }

    internal class ThrowConflictResolver : IResolveConflicts
    {
        public Task Resolve<TEntity, TState>(TEntity entity, IFullEvent[] uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : IState, new()
        {
            throw new ConflictResolutionFailedException("No conflict resolution attempted");
        }
    }

    /// <summary>
    /// Conflict from the store is ignored, events will always be written
    /// </summary>
    internal class IgnoreConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogProvider.GetLogger("IgnoreConflictResolver");

        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _streamGen;

        public IgnoreConflictResolver(IStoreEvents store, StreamIdGenerator streamGen)
        {
            _store = store;
            _streamGen = streamGen;
        }

        public async Task Resolve<TEntity, TState>(TEntity entity, IFullEvent[] uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : IState, new()
        {
            var state = entity.State;

            Logger.DebugEvent("Resolver", "Resolving {Events} conflicting events to stream [{Stream:l}] type [{EntityType:l}] bucket [{Bucket:l}]", uncommitted.Count(), entity.Id, typeof(TEntity).FullName, entity.Bucket);

            foreach (var u in uncommitted)
            {
                state.Apply(u.Event as IEvent);
            }

            await _store.WriteEvents<TEntity>(entity.Bucket, entity.Id, entity.Parents, uncommitted, commitHeaders).ConfigureAwait(false);
        }
    }
    /// <summary>
    /// Conflicted events are discarded
    /// </summary>
    internal class DiscardConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogProvider.GetLogger("DiscardConflictResolver");

        public Task Resolve<TEntity, TState>(TEntity entity, IFullEvent[] uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : IState, new()
        {
            Logger.DebugEvent("Resolver", "Discarding {Events} conflicting events to stream [{Stream:l}] type [{EntityType:l}] bucket [{Bucket:l}]", uncommitted.Count(), entity.Id, typeof(TEntity).FullName, entity.Bucket);

            return Task.CompletedTask;
        }
    }
    /// <summary>
    /// Pull latest events from store, merge into stream and re-commit
    /// </summary>
    internal class ResolveStronglyConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogProvider.GetLogger("ResolveStronglyConflictResolver");

        private readonly IStoreSnapshots _snapstore;
        private readonly IStoreEvents _eventstore;
        private readonly StreamIdGenerator _streamGen;

        public ResolveStronglyConflictResolver(IStoreSnapshots snapshot, IStoreEvents eventstore, StreamIdGenerator streamGen)
        {
            _snapstore = snapshot;
            _eventstore = eventstore;
            _streamGen = streamGen;
        }

        public async Task Resolve<TEntity, TState>(TEntity entity, IFullEvent[] uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : IState, new()
        {
            var state = entity.State;
            Logger.DebugEvent("Resolver", "Resolving {Events} conflicting events to stream [{Stream:l}] type [{EntityType:l}] bucket [{Bucket:l}]", uncommitted.Count(), entity.Id, typeof(TEntity).FullName, entity.Bucket);

            var latestEvents =
                await _eventstore.GetEvents<TEntity>(entity.Bucket, entity.Id, entity.Parents, entity.Version).ConfigureAwait(false);
            Logger.DebugEvent("Behind", "Stream is {Count} events behind store", latestEvents.Count());

            for (var i = 0; i < latestEvents.Length; i++)
                state.Apply(latestEvents[i].Event as IEvent);

            try
            {
                foreach (var u in uncommitted)
                {
                    if (u.Descriptor.StreamType == StreamTypes.Domain)
                        entity.Conflict(u.Event as IEvent);
                    else if (u.Descriptor.StreamType == StreamTypes.OOB)
                    {
                        // Todo: small hack
                        string id = "";
                        bool transient = true;
                        int daysToLive = -1;

                        id = u.Descriptor.Headers[Defaults.OobHeaderKey];

                        if (u.Descriptor.Headers.ContainsKey(Defaults.OobTransientKey))
                            bool.TryParse(u.Descriptor.Headers[Defaults.OobTransientKey], out transient);
                        if (u.Descriptor.Headers.ContainsKey(Defaults.OobDaysToLiveKey))
                            int.TryParse(u.Descriptor.Headers[Defaults.OobDaysToLiveKey], out daysToLive);

                        entity.Raise(u.Event as IEvent, id, transient, daysToLive);
                    }
                }
            }
            catch (NoRouteException e)
            {
                Logger.WarnEvent("ResolveFailure", e, "Failed to resolve conflict: {ExceptionType} - {ExceptionMessage}", e.GetType().Name, e.Message);
                throw new ConflictResolutionFailedException("Failed to resolve conflict", e);
            }

            await _eventstore.WriteEvents<TEntity>(entity.Bucket, entity.Id, entity.Parents, entity.Uncommitted, commitHeaders).ConfigureAwait(false);
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
        internal static readonly ILog Logger = LogProvider.GetLogger("ResolveWeaklyConflictResolver");

        private readonly IStoreSnapshots _snapstore;
        private readonly IStoreEvents _eventstore;
        private readonly IDelayedChannel _delay;
        private readonly StreamIdGenerator _streamGen;


        public ResolveWeaklyConflictResolver(IStoreSnapshots snapstore, IStoreEvents eventstore, IDelayedChannel delay, StreamIdGenerator streamGen)
        {
            _snapstore = snapstore;
            _eventstore = eventstore;
            _delay = delay;
            _streamGen = streamGen;
        }


        public Task Resolve<TEntity, TState>(TEntity entity, IFullEvent[] uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity<TState> where TState : IState, new()
        {
            throw new NotImplementedException();
        }
    }




}
