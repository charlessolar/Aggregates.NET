using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Metrics;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System.Linq;

namespace Aggregates.Internal
{
    class StorePocos : IStorePocos
    {
        private static readonly Meter HitMeter = Metric.Meter("Poco Cache Hits", Unit.Events);
        private static readonly Meter MissMeter = Metric.Meter("Poco Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreStreams));
        private readonly IStoreEvents _store;
        private readonly IStreamCache _cache;
        private readonly bool _shouldCache;
        private readonly StreamIdGenerator _streamGen;

        public IBuilder Builder { get; set; }

        public StorePocos(IStoreEvents store, IStreamCache cache, bool shouldCache, StreamIdGenerator streamGen)
        {
            _store = store;
            _cache = cache;
            _shouldCache = shouldCache;
            _streamGen = streamGen;
        }

        public Task Evict<T>(string bucket, string streamId) where T : class
        {
            var streamName = _streamGen(typeof(T), bucket + ".POCO", streamId);
            _cache.Evict(streamName);
            return Task.CompletedTask;
        }


        public async Task<T> Get<T>(string bucket, string stream) where T : class
        {
            var streamName = $"{_streamGen(typeof(T), bucket + ".POCO", stream)}";
            Logger.Write(LogLevel.Debug, () => $"Getting stream [{streamName}]");

            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as T;
                if (cached != null)
                {
                    HitMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Found poco [{stream}] bucket [{bucket}] in cache");
                    // An easy way to make a deep copy
                    return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(cached));
                }
                MissMeter.Mark();
            }

            var read = await _store.GetEventsBackwards(streamName, StreamPosition.End, 1).ConfigureAwait(false);
            
            if (read == null || !read.Any())
                return null;

            var @event = read.Single();

            if (_shouldCache)
                _cache.Cache(streamName, @event.Event);
            return @event.Event as T;
        }
        public async Task Write<T>(T poco, string bucket, string stream, IDictionary<string, string> commitHeaders)
        {
            var streamName = $"{_streamGen(typeof(T), bucket + ".POCO", stream)}";
            Logger.Write(LogLevel.Debug, () => $"Writing poco to stream id [{streamName}]");

            var descriptor = new EventDescriptor
            {
                EntityType = typeof(T).AssemblyQualifiedName,
                Timestamp = DateTime.UtcNow,
                Version = -1,
                Headers = commitHeaders
            };

            var @event = new WritableEvent
            {
                Descriptor = descriptor,
                Event = poco,
                EventId = Guid.NewGuid()
            };
            
            if(await _store.WriteEvents(streamName, new[] {@event}, commitHeaders).ConfigureAwait(false) == 1)
                await _store.WriteMetadata(streamName, maxCount: 10).ConfigureAwait(false);

            if (_shouldCache)
                _cache.Cache(streamName, poco);
        }
    }
}
