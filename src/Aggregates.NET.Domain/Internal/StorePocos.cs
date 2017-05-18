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
        private static readonly Meter Saved = Metric.Meter("Saved Pocos", Unit.Items, tags: "debug");
        private static readonly Meter HitMeter = Metric.Meter("Poco Cache Hits", Unit.Events, tags: "debug");
        private static readonly Meter MissMeter = Metric.Meter("Poco Cache Misses", Unit.Events, tags: "debug");

        private static readonly ILog Logger = LogManager.GetLogger("StoreStreams");
        private readonly IStoreEvents _store;
        private readonly ICache _cache;
        private readonly bool _shouldCache;
        private readonly StreamIdGenerator _streamGen;

        public IBuilder Builder { get; set; }

        public StorePocos(IStoreEvents store, ICache cache, bool shouldCache, StreamIdGenerator streamGen)
        {
            _store = store;
            _cache = cache;
            _shouldCache = shouldCache;
            _streamGen = streamGen;
        }
        

        public async Task<Tuple<long,T>> Get<T>(string bucket, Id streamId, IEnumerable<Id> parents) where T : class
        {
            var streamName = _streamGen(typeof(T), StreamTypes.Poco, bucket, streamId, parents);
            Logger.Write(LogLevel.Debug, () => $"Getting poco stream [{streamName}]");

            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as Tuple<long,T>;
                if (cached != null)
                {
                    HitMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Found poco [{streamId}] bucket [{bucket}] version {cached.Item1} in cache");
                    return new Tuple<long, T>(cached.Item1,
                    // An easy way to make a deep copy
                        JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(cached.Item2)));
                }
                MissMeter.Mark();
            }
            
            var read = await _store.GetEventsBackwards(streamName, StreamPosition.End, 1).ConfigureAwait(false);

            if (read == null || !read.Any())
                return null;

            var @event = read.Single();

            if (_shouldCache)
                _cache.Cache(streamName, @event.Event);
            return new Tuple<long, T>(@event.Descriptor.Version, @event.Event as T);
        }
        public async Task Write<T>(Tuple<long,T> poco, string bucket, Id streamId, IEnumerable<Id> parents, IDictionary<string, string> commitHeaders)
        {
            var streamName = _streamGen(typeof(T), StreamTypes.Poco, bucket, streamId, parents);
            Logger.Write(LogLevel.Debug, () => $"Writing poco to stream id [{streamName}]");

            var descriptor = new EventDescriptor
            {
                EntityType = typeof(T).AssemblyQualifiedName,
                StreamType = StreamTypes.Poco,
                Bucket = bucket,
                StreamId = streamId,
                Parents = parents,
                Timestamp = DateTime.UtcNow,
                // When reading version will be the stream position
                Version = 0,
                Headers = new Dictionary<string, string>(),
                CommitHeaders = commitHeaders
            };

            var @event = new WritableEvent
            {
                Descriptor = descriptor,
                Event = poco.Item2,
                EventId = Guid.NewGuid()
            };

            Saved.Mark();
            if (await _store.WriteEvents(streamName, new IFullEvent[] { @event }, commitHeaders, expectedVersion: poco.Item1).ConfigureAwait(false) == 1)
                await _store.WriteMetadata(streamName, maxCount: 5).ConfigureAwait(false);

            if (_shouldCache)
                _cache.Cache(streamName, poco);
        }
    }
}
