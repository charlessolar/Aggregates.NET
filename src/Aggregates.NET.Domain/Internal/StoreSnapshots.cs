using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Metrics;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    class StoreSnapshots : IStoreSnapshots
    {
        private static readonly Meter Saved = Metric.Meter("Saved Snapshots", Unit.Items);
        private static readonly Meter HitMeter = Metric.Meter("Snapshot Cache Hits", Unit.Events);
        private static readonly Meter MissMeter = Metric.Meter("Snapshot Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger("StoreSnapshots");
        private readonly IStoreEvents _store;
        private readonly ISnapshotReader _snapshots;
        private readonly StreamIdGenerator _streamGen;

        public StoreSnapshots(IStoreEvents store, ISnapshotReader snapshots, StreamIdGenerator streamGen)
        {
            _store = store;
            _snapshots = snapshots;
            _streamGen = streamGen;
        }

        public async Task<ISnapshot> GetSnapshot<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.Snapshot, bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Getting snapshot for stream [{streamName}]");
            {
                var snapshot = await _snapshots.Retreive(streamName).ConfigureAwait(false);
                if (snapshot != null)
                {
                    HitMeter.Mark();
                    Logger.Write(LogLevel.Debug,
                        () => $"Found snapshot [{streamName}] version {snapshot.Version} from subscription");
                    return snapshot;
                }
            }

            // Check store directly (this might be a new instance which hasn't caught up to snapshot stream yet

            Logger.Write(LogLevel.Debug, () => $"Checking for snapshot for stream [{streamName}] in store");

            var read = await _store.GetEventsBackwards(streamName, StreamPosition.End, 1).ConfigureAwait(false);

            if (read != null && read.Any())
            {
                var @event = read.Single();
                var snapshot = new Snapshot
                {
                    EntityType = @event.Descriptor.EntityType,
                    Bucket = bucket,
                    StreamId = streamId,
                    Timestamp = @event.Descriptor.Timestamp,
                    Version = @event.Descriptor.Version,
                    Payload = @event.Event
                };
                HitMeter.Mark();
                Logger.Write(LogLevel.Debug, () => $"Found snapshot [{streamName}] version {snapshot.Version} from store");
                return snapshot;
            }

            MissMeter.Mark();
            Logger.Write(LogLevel.Debug, () => $"Snapshot [{streamName}] not found");
            return null;
        }


        public async Task WriteSnapshots<T>(string bucket, string streamId, ISnapshot snapshot, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.Snapshot, bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Writing snapshot to stream [{streamName}]");

            var e = new WritableEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).AssemblyQualifiedName,
                    StreamType = StreamTypes.Snapshot,
                    Bucket = bucket,
                    StreamId = streamId,
                    Timestamp = snapshot.Timestamp,
                    Version = snapshot.Version,
                    Headers = new Dictionary<string, string>(),
                    CommitHeaders = commitHeaders
                },
                Event = snapshot.Payload,
            };

            Saved.Mark();
            if (await _store.WriteSnapshot(streamName, e, commitHeaders).ConfigureAwait(false) == 1)
                // New stream, write metadata
                await _store.WriteMetadata(streamName, maxCount: 5).ConfigureAwait(false);

        }

    }
}
