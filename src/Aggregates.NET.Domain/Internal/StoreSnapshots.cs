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

            var @event = await _snapshots.Retreive(streamName).ConfigureAwait(false);
            if (@event == null)
            {
                MissMeter.Mark();
                Logger.Write(LogLevel.Debug, () => $"Snapshot [{streamName}] not in store");
                return null;
            }

            var snapshot = new Snapshot
            {
                EntityType = @event.Descriptor.EntityType,
                Bucket = bucket,
                Stream = streamId,
                Timestamp = @event.Descriptor.Timestamp,
                Version = @event.Descriptor.Version,
                Payload = @event.Event
            };

            HitMeter.Mark();
            Logger.Write(LogLevel.Debug, () => $"Found snapshot [{streamName}] version {snapshot.Version} in store!");
            return snapshot;
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
                    Timestamp = snapshot.Timestamp,
                    Version = snapshot.Version,
                    Headers = commitHeaders
                },
                Event = snapshot.Payload,
            };

            Saved.Mark();
            if (await _store.WriteSnapshot(streamName, e, commitHeaders).ConfigureAwait(false) == 1)
                // New stream, write metadata
                await _store.WriteMetadata(streamName, maxCount: 10).ConfigureAwait(false);
            
        }
        
    }
}
