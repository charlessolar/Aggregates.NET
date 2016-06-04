using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
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
        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreSnapshots));
        private readonly IEventStoreConnection _client;
        private readonly ReadOnlySettings _nsbSettings;
        private readonly IStreamCache _cache;
        private readonly Boolean _shouldCache;
        private readonly JsonSerializerSettings _settings;

        public StoreSnapshots(IEventStoreConnection client, ReadOnlySettings nsbSettings, IStreamCache cache, JsonSerializerSettings settings)
        {
            _client = client;
            _nsbSettings = nsbSettings;
            _settings = settings;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<Boolean>("ShouldCacheEntities");
        }

        public async Task<ISnapshot> GetSnapshot(String bucket, String stream)
        {
            Logger.DebugFormat("Getting snapshot for stream [{0}] in bucket [{1}]", stream, bucket);

            var streamId = String.Format("{0}.{1}.{2}", bucket, stream, "snapshots");

            if (_shouldCache)
            {
                var cached = _cache.RetreiveSnap(streamId);
                if (cached != null)
                    return cached;
            }

            var read = await _client.ReadEventAsync(streamId, StreamPosition.End, false);
            if (read.Status != EventReadStatus.Success || !read.Event.HasValue)
                return null;

            var @event = read.Event.Value.Event;

            var descriptor = @event.Metadata.Deserialize(_settings);
            var data = @event.Data.Deserialize(descriptor.EntityType, _settings);

            var snapshot = new Snapshot
            {
                EntityType = descriptor.EntityType,
                Bucket = bucket,
                Stream = stream,
                Timestamp = descriptor.Timestamp,
                Version = descriptor.Version,
                Payload = data
            };
            if (_shouldCache)
                _cache.CacheSnap(streamId, snapshot);
            return snapshot;
        }


        public async Task WriteSnapshots(String bucket, String stream, IEnumerable<ISnapshot> snapshots, IDictionary<String, String> commitHeaders)
        {
            Logger.DebugFormat("Writing {0} snapshots to stream id [{1}] in bucket [{2}]", snapshots.Count(), stream, bucket);
            var streamId = String.Format("{0}.{1}.{2}", bucket, stream, "snapshots");

            if (_shouldCache)
                _cache.EvictSnap(streamId);

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

            
            await _client.AppendToStreamAsync(streamId, ExpectedVersion.Any, translatedEvents);
        }
        
    }
}
