using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class StoreSnapshots : IStoreSnapshots
    {
        private static readonly ILog Logger = LogProvider.GetLogger("StoreSnapshots");

        private readonly IMetrics _metrics;
        private readonly IStoreEvents _store;
        private readonly ISnapshotReader _snapshots;
        private readonly IVersionRegistrar _registrar;
        private readonly StreamIdGenerator _streamGen;

        public StoreSnapshots(IMetrics metrics, IStoreEvents store, ISnapshotReader snapshots, IVersionRegistrar registrar)
        {
            _metrics = metrics;
            _store = store;
            _snapshots = snapshots;
            _registrar = registrar;
            _streamGen = Configuration.Settings.Generator;
        }

        public async Task<ISnapshot> GetSnapshot<T>(string bucket, Id streamId, Id[] parents) where T : IEntity
        {
            var streamName = _streamGen(_registrar.GetVersionedName(typeof(T)), StreamTypes.Snapshot, bucket, streamId, parents);

            Logger.DebugEvent("Get", "[{Stream:l}]", streamName);
            if (_snapshots != null)
            {
                var snapshot = await _snapshots.Retreive(streamName).ConfigureAwait(false);
                if (snapshot != null)
                {
                    _metrics.Mark("Snapshot Cache Hits", Unit.Items);
                    Logger.DebugEvent("Cached", "[{Stream:l}] version {Version}", streamName, snapshot.Version);
                    return snapshot;
                }
            }
            _metrics.Mark("Snapshot Cache Misses", Unit.Items);

            // Check store directly (this might be a new instance which hasn't caught up to snapshot stream yet
            

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
                    Payload = @event.Event as IState
                };
                Logger.DebugEvent("Read", "[{Stream:l}] version {Version}", streamName, snapshot.Version);
                return snapshot;
            }
            
            Logger.DebugEvent("NotFound", "[{Stream:l}]", streamName);
            return null;
        }


        public async Task WriteSnapshots<T>(IState snapshot, IDictionary<string, string> commitHeaders) where T : IEntity
        {
            
            var streamName = _streamGen(_registrar.GetVersionedName(typeof(T)), StreamTypes.Snapshot, snapshot.Bucket, snapshot.Id, snapshot.Parents.Select(x => x.StreamId).ToArray());
            Logger.DebugEvent("Write", "[{Stream:l}]", streamName);

            // We don't need snapshots to store the previous snapshot
            // ideally this field would be [JsonIgnore] but we have no dependency on json.net
            snapshot.Snapshot = null;
            
            var e = new FullEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = _registrar.GetVersionedName(typeof(T)),
                    StreamType = StreamTypes.Snapshot,
                    Bucket = snapshot.Bucket,
                    StreamId = snapshot.Id,
                    Parents = snapshot.Parents,
                    Timestamp = DateTime.UtcNow,
                    Version = snapshot.Version + 1,
                    Headers = new Dictionary<string, string>(),
                    CommitHeaders = commitHeaders
                },
                Event = snapshot,
            };
            
            await _store.WriteEvents(streamName, new[] { e }, commitHeaders).ConfigureAwait(false);

        }

    }
}
