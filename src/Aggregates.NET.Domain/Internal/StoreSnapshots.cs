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
        private static readonly Meter HitMeter = Metric.Meter("Snapshot Cache Hits", Unit.Events);
        private static readonly Meter MissMeter = Metric.Meter("Snapshot Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreSnapshots));
        private readonly IStoreEvents _store;
        private readonly IStreamCache _cache;
        private readonly bool _shouldCache;
        private readonly StreamIdGenerator _streamGen;

        public StoreSnapshots(IStoreEvents store, IStreamCache cache, bool shouldCache, StreamIdGenerator streamGen)
        {
            _store = store;
            _cache = cache;
            _shouldCache = shouldCache;
            _streamGen = streamGen;
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


            var read = await _store.GetEvents(streamName, StreamPosition.End, 1).ConfigureAwait(false);

            if (read == null || !read.Any())
                return null;


            var @event = read.Single();
            var snapshot = new Snapshot
            {
                EntityType = @event.Descriptor.EntityType,
                Bucket = bucket,
                Stream = streamId,
                Timestamp = @event.Descriptor.Timestamp,
                Version = @event.Descriptor.Version,
                Payload = @event.Event
            };

            if (_shouldCache)
                _cache.Cache(streamName, snapshot);
            return snapshot;
            
        }


        public async Task WriteSnapshots<T>(string bucket, string streamId, IEnumerable<ISnapshot> snapshots, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = $"{_streamGen(typeof(T), bucket + ".SNAP", streamId)}";
            Logger.Write(LogLevel.Debug, () => $"Writing {snapshots.Count()} snapshots to stream [{streamName}]");
            

            var translatedEvents = snapshots.Select(e =>
            {
                var descriptor = new EventDescriptor
                {
                    EntityType = typeof(T).AssemblyQualifiedName,
                    Timestamp = e.Timestamp,
                    Version = e.Version,
                    Headers = commitHeaders
                };

                return new WritableEvent
                {
                    Descriptor = descriptor,
                    Event = e.Payload,
                    EventId = Guid.NewGuid()
                };
            }).ToList();


            if (await _store.WriteEvents(streamName, translatedEvents, commitHeaders).ConfigureAwait(false) == 1)
                await _store.WriteMetadata(streamName, maxCount: 10).ConfigureAwait(false);
            
            if (_shouldCache)
                _cache.Cache(streamName, snapshots.Last());
        }
        
    }
}
