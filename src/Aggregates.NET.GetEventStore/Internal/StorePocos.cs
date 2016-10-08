using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    internal class StorePocos : IStorePocos
    {
        private static readonly Meter HitMeter = Metric.Meter("Poco Cache Hits", Unit.Events);
        private static readonly Meter MissMeter = Metric.Meter("Poco Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreEvents));
        private readonly IEventStoreConnection _client;
        private readonly ReadOnlySettings _nsbSettings;
        private readonly IStreamCache _cache;
        private readonly bool _shouldCache;
        private readonly JsonSerializerSettings _settings;
        private readonly StreamIdGenerator _streamGen;

        public IBuilder Builder { get; set; }

        public StorePocos(IEventStoreConnection client, ReadOnlySettings nsbSettings, IStreamCache cache, JsonSerializerSettings settings)
        {
            _client = client;
            _nsbSettings = nsbSettings;
            _settings = settings;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<bool>("ShouldCacheEntities");
            _streamGen = _nsbSettings.Get<StreamIdGenerator>("StreamGenerator");
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

            var read = await _client.ReadEventAsync(streamName, StreamPosition.End, false).ConfigureAwait(false);
            if (read.Status != EventReadStatus.Success || !read.Event.HasValue)
                return null;


            var compress = _nsbSettings.Get<bool>("Compress");

            var @event = read.Event.Value.Event;
            var metadata = @event.Metadata;
            var data = @event.Data;
            if (compress)
                data = data.Decompress();

            
            var result = data.Deserialize<T>(_settings);

            if (_shouldCache)
                _cache.Cache(streamName, result);
            return result;
        }
        public async Task Write<T>(T poco, string bucket, string stream, IDictionary<string, string> commitHeaders)
        {
            var streamName = $"{_streamGen(typeof(T), bucket + ".POCO", stream)}";
            Logger.Write(LogLevel.Debug, () => $"Writing poco to stream id [{streamName}]");

            var compress = _nsbSettings.Get<bool>("Compress");

            var descriptor = new EventDescriptor
            {
                EntityType = typeof(T).FullName,
                Timestamp = DateTime.UtcNow,
                Version = -1,
                Headers = commitHeaders
            };
            var @event = poco.Serialize(_settings).AsByteArray();
            var metadata = descriptor.Serialize(_settings).AsByteArray();
            if (compress)
            {
                @event = @event.Compress();
                metadata = metadata.Compress();
            }

            var translatedEvent = new EventData(
                    Guid.NewGuid(),
                    typeof(T).AssemblyQualifiedName,
                    !compress,
                    @event,
                    metadata
                );

            var result = await _client.AppendToStreamAsync(streamName, ExpectedVersion.Any, translatedEvent).ConfigureAwait(false);
            if (result.NextExpectedVersion == 1)
            {
                Logger.Write(LogLevel.Debug, () => $"Writing metadata to snapshot stream id [{streamName}]");

                var streamMetadata = StreamMetadata.Create(maxCount: 10);

                await _client.SetStreamMetadataAsync(streamName, ExpectedVersion.Any, streamMetadata).ConfigureAwait(false);
            }
        }
    }
}
