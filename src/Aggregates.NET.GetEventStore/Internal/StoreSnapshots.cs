using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    internal class StoreSnapshots : IStoreSnapshots
    {
        private static readonly Meter HitMeter = Metric.Meter("Snapshot Cache Hits", Unit.Events);
        private static readonly Meter MissMeter = Metric.Meter("Snapshot Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreSnapshots));
        private readonly IEventStoreConnection _client;
        private readonly ReadOnlySettings _nsbSettings;
        private readonly IStreamCache _cache;
        private readonly bool _shouldCache;
        private readonly JsonSerializerSettings _settings;
        private readonly StreamIdGenerator _streamGen;

        public StoreSnapshots(IEventStoreConnection client, ReadOnlySettings nsbSettings, IStreamCache cache, JsonSerializerSettings settings)
        {
            _client = client;
            _nsbSettings = nsbSettings;
            _settings = settings;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<bool>("ShouldCacheEntities");
            _streamGen = _nsbSettings.Get<StreamIdGenerator>("StreamGenerator");
        }

        public Task Evict<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket + ".SNAP", streamId);
            _cache.Evict(streamName);
            return Task.CompletedTask;
        }

        public async Task<ISnapshot> GetSnapshot<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = $"{_streamGen(typeof(T), bucket + ".SNAP", streamId)}";
            Logger.Write(LogLevel.Debug, () => $"Getting snapshot for stream [{streamName}]");

            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as ISnapshot;
                if (cached != null)
                {
                    HitMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Found snapshot [{streamName}] in cache");
                    return cached;
                }
                MissMeter.Mark();
            }
            Logger.Write(LogLevel.Debug, () => $"Reading snapshot for stream [{streamName}] from store");

            var read = await _client.ReadEventAsync(streamName, StreamPosition.End, false).ConfigureAwait(false);
            if (read.Status != EventReadStatus.Success || !read.Event.HasValue)
                return null;

            var compress = _nsbSettings.Get<bool>("Compress");

            var @event = read.Event.Value.Event;
            var metadata = @event.Metadata;
            var data = @event.Data;
            if (compress)
            {
                metadata = metadata.Decompress();
                data = data.Decompress();
            }

            var descriptor = @event.Metadata.Deserialize(_settings);
            var result = data.Deserialize(@event.EventType, _settings);
            var snapshot = new Snapshot
            {
                EntityType = descriptor.EntityType,
                Bucket = bucket,
                Stream = streamId,
                Timestamp = descriptor.Timestamp,
                Version = descriptor.Version,
                Payload = result
            };


            if (_shouldCache)
                _cache.Cache(streamName, snapshot);
            return snapshot;
        }


        public async Task WriteSnapshots<T>(string bucket, string streamId, IEnumerable<ISnapshot> snapshots, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = $"{_streamGen(typeof(T), bucket + ".SNAP", streamId)}";
            Logger.Write(LogLevel.Debug, () => $"Writing {snapshots.Count()} snapshots to stream [{streamName}]");

            var compress = _nsbSettings.Get<bool>("Compress");

            var translatedEvents = snapshots.Select(e =>
            {
                var descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).AssemblyQualifiedName,
                    Timestamp = e.Timestamp,
                    Version = e.Version,
                    Headers = commitHeaders
                };

                var @event = e.Payload.Serialize(_settings).AsByteArray();
                var metadata = descriptor.Serialize(_settings).AsByteArray();
                if (compress)
                {
                    @event = @event.Compress();
                    metadata = metadata.Compress();
                }
                return new EventData(
                    Guid.NewGuid(),
                    e.Payload.GetType().AssemblyQualifiedName,
                    !compress,
                    @event,
                    metadata
                    );
            }).ToList();

            
            var result = await _client.AppendToStreamAsync(streamName, ExpectedVersion.Any, translatedEvents).ConfigureAwait(false);
            if( result.NextExpectedVersion == 1)
            {
                Logger.Write(LogLevel.Debug, () => $"Writing metadata to snapshot stream [{streamName}]");

                var metadata = StreamMetadata.Create(maxCount: 10);

                await _client.SetStreamMetadataAsync(streamName, ExpectedVersion.Any, metadata).ConfigureAwait(false);
            }
            if (_shouldCache)
                _cache.Cache(streamName, snapshots.Last());
        }
        
    }
}
