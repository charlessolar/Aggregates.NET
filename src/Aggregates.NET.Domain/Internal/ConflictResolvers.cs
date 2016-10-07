using Aggregates.Contracts;
using Aggregates.Extensions;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    /// <summary>
    /// Conflict from the store is ignored, events will always be written
    /// </summary>
    internal class IgnoreConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(IgnoreConflictResolver));

        private readonly IStoreEvents _store;

        public IgnoreConflictResolver(IStoreEvents eventstore)
        {
            _store = eventstore;
        }

        public async Task<Guid> Resolve<T>(T Entity, IEnumerable<IWritableEvent> Uncommitted, Guid commitId, Guid startingEventId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var stream = Entity.Stream;
            Logger.Write(LogLevel.Info, () => $"Resolving {Uncommitted.Count()} uncommitted events to stream {stream.StreamId} bucket {stream.Bucket}");

            foreach (var uncommitted in Uncommitted)
            {
                if (!uncommitted.EventId.HasValue)
                {
                    uncommitted.EventId = startingEventId;
                    startingEventId = startingEventId.Increment();
                }
                Entity.Apply(uncommitted.Event);
            }

            await _store.AppendEvents<T>(stream.Bucket, stream.StreamId, Uncommitted, commitHeaders).ConfigureAwait(false);
            stream.Flush(true);

            return startingEventId;
        }
    }
    internal class DiscardConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(DiscardConflictResolver));
        
        public Task<Guid> Resolve<T>(T Entity, IEnumerable<IWritableEvent> Uncommitted, Guid commitId, Guid startingEventId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var stream = Entity.Stream;
            Logger.Write(LogLevel.Info, () => $"Resolving {Uncommitted.Count()} uncommitted events to stream {stream.StreamId} bucket {stream.Bucket}");

            return Task.FromResult(startingEventId);
        }
    }
    internal class ResolveStronglyConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(ResolveStronglyConflictResolver));

        private readonly IStoreEvents _store;

        public ResolveStronglyConflictResolver(IStoreEvents eventstore)
        {
            _store = eventstore;
        }

        public async Task<Guid> Resolve<T>(T Entity, IEnumerable<IWritableEvent> Uncommitted, Guid commitId, Guid startingEventId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var stream = Entity.Stream;
            Logger.Write(LogLevel.Info, () => $"Resolving {Uncommitted.Count()} uncommitted events to stream {stream.StreamId} bucket {stream.Bucket}");

            var latestEvents = await _store.GetEvents<T>(stream.Bucket, stream.StreamId, stream.CommitVersion + 1).ConfigureAwait(false);
            Logger.Write(LogLevel.Debug, () => $"Stream is {latestEvents.Count()} events behind store");

            stream.Concat(latestEvents);
            Entity.Hydrate(latestEvents.Select(x => x.Event));
            

            Logger.Write(LogLevel.Debug, () => $"Merging conflicted events");
            foreach (var uncommitted in Uncommitted)
                Entity.Conflict(uncommitted.Event);

            Logger.Write(LogLevel.Debug, () => $"Successfully merged conflicted events");

            if (stream.StreamVersion != stream.CommitVersion && Entity is ISnapshotting && (Entity as ISnapshotting).ShouldTakeSnapshot())
            {
                Logger.Write(LogLevel.Debug, () => $"Taking snapshot of {typeof(T).FullName} id [{Entity.StreamId}] version {stream.StreamVersion}");
                var memento = (Entity as ISnapshotting).TakeSnapshot();
                stream.AddSnapshot(memento, commitHeaders);
            }

            startingEventId = await stream.Commit(commitId, startingEventId, commitHeaders);


            await _store.AppendEvents<T>(stream.Bucket, stream.StreamId, Uncommitted, commitHeaders).ConfigureAwait(false);
            stream.Flush(true);

            return startingEventId;
        }
    }
    internal class ResolveWeaklyConflictResolver : IResolveConflicts
    {
        internal static readonly ILog Logger = LogManager.GetLogger(typeof(ResolveWeaklyConflictResolver));

        private readonly IStoreEvents _store;

        public ResolveWeaklyConflictResolver(IStoreEvents eventstore)
        {
            _store = eventstore;
        }

        public Task<Guid> Resolve<T>(T Entity, IEnumerable<IWritableEvent> Uncommitted, Guid commitId, Guid startingEventId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            // Store conflicting events in memory
            // After set timeout (default 30s) pull the latest stream and attempt to write them again
            // Perhaps we can leverage bus.DelayedSend, then during times of high load conflicts will wait to be resolved until the message queue is empty
            // might not be the best idea though - as we would have to send to our exact instance which will usually be an empty queue

            throw new NotImplementedException();
        }
    }

}
