using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Metrics;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class StoreSnapshots : IStoreSnapshots
    {
        private static Meter _hitMeter = Metric.Meter("Snapshot Cache Hits", Unit.Events);
        private static Meter _missMeter = Metric.Meter("Snapshot Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreSnapshots));
        private readonly IEventStoreConnection _client;
        private readonly ReadOnlySettings _nsbSettings;
        private readonly IStreamCache _cache;
        private readonly Boolean _shouldCache;
        private readonly JsonSerializerSettings _settings;
        private readonly StreamIdGenerator _streamGen;

        public StoreSnapshots(IEventStoreConnection client, ReadOnlySettings nsbSettings, IStreamCache cache, JsonSerializerSettings settings)
        {
            _client = client;
            _nsbSettings = nsbSettings;
            _settings = settings;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<Boolean>("ShouldCacheEntities");
            _streamGen = _nsbSettings.Get<StreamIdGenerator>("StreamGenerator");
        }

        public async Task<ISnapshot> GetSnapshot<T>(String bucket, String streamId) where T : class, IEventSource
        {
            Logger.Write(LogLevel.Debug, () => $"Getting snapshot for stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName}");

            var streamName = $"{_streamGen(typeof(T), bucket + ".SNAP", streamId)}";

            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as ISnapshot;
                if (cached != null)
                {
                    _hitMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Found snapshot [{streamId}] bucket [{bucket}] in cache");
                    return cached;
                }
                _missMeter.Mark();
            }

            var read = await _client.ReadEventAsync(streamName, StreamPosition.End, false);
            if (read.Status != EventReadStatus.Success || !read.Event.HasValue)
                return null;

            var @event = read.Event.Value.Event;

            var descriptor = @event.Metadata.Deserialize(_settings);
            var data = @event.Data.Deserialize(descriptor.EntityType, _settings);
            
            var snapshot = new Snapshot
            {
                EntityType = descriptor.EntityType,
                Bucket = bucket,
                Stream = streamId,
                Timestamp = descriptor.Timestamp,
                Version = descriptor.Version,
                Payload = data
            };
            if (_shouldCache)
                _cache.Cache(streamName, snapshot);
            return snapshot;
        }


        public async Task WriteSnapshots<T>(String bucket, String streamId, IEnumerable<ISnapshot> snapshots, IDictionary<String, String> commitHeaders) where T : class, IEventSource
        {
            Logger.Write(LogLevel.Debug, () => $"Writing {snapshots.Count()} snapshots to stream id [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName}");
            var streamName = $"{_streamGen(typeof(T), bucket + ".SNAP", streamId)}";


            var translatedEvents = snapshots.Select(e =>
            {
                var descriptor = new EventDescriptor
                {
                    EntityType = e.EntityType,
                    Timestamp = e.Timestamp,
                    Version = e.Version,
                    Headers = commitHeaders
                };
                return new EventData(
                    Guid.NewGuid(),
                    e.EntityType,
                    true,
                    e.Payload.Serialize(_settings).AsByteArray(),
                    descriptor.Serialize(_settings).AsByteArray()
                    );
            }).ToList();

            
            await _client.AppendToStreamAsync(streamName, ExpectedVersion.Any, translatedEvents);
        }
        
    }
}
