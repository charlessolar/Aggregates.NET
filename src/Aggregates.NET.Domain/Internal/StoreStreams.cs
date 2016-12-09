using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Metrics;
using Metrics.Utils;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    class StoreStreams : IStoreStreams
    {
        private static readonly Meter Saved = Metric.Meter("Saved Streams", Unit.Items);
        private static readonly Meter HitMeter = Metric.Meter("Stream Cache Hits", Unit.Events);
        private static readonly Meter MissMeter = Metric.Meter("Stream Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreStreams));
        private readonly IStoreEvents _store;
        private readonly IStreamCache _cache;
        private readonly bool _shouldCache;
        private readonly StreamIdGenerator _streamGen;

        public IBuilder Builder { get; set; }

        public StoreStreams(IStoreEvents store, IStreamCache cache, bool cacheStreams, StreamIdGenerator streamGen)
        {
            _store = store;
            _cache = cache;
            _shouldCache = cacheStreams;
            _streamGen = streamGen;
        }

        public Task Evict<T>(string bucket, string streamId) where T : class, IEventSource
        {
            if (!_shouldCache) return Task.CompletedTask;

            var streamName = _streamGen(typeof(T), bucket, streamId);
            _cache.Evict(streamName);
            return Task.CompletedTask;
        }
        public Task Cache<T>(IEventStream stream) where T : class, IEventSource
        {
            if (!_shouldCache) return Task.CompletedTask;

            var streamName = _streamGen(typeof(T), stream.Bucket, stream.StreamId);
            _cache.Cache(streamName, stream.Clone());
            return Task.CompletedTask;
        }

        public async Task<IEventStream> GetStream<T>(string bucket, string streamId, ISnapshot snapshot = null) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            
            Logger.Write(LogLevel.Debug, () => $"Retreiving stream [{streamId}] in bucket [{bucket}]");
            
            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as EventStream<T>;
                if (cached != null && cached.CommitVersion >= (snapshot?.Version + 1))
                {
                    HitMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Found stream [{streamName}] in cache");
                    return new EventStream<T>(cached, Builder, this, snapshot);
                }
                MissMeter.Mark();
            }

            while (await CheckFrozen<T>(bucket, streamId).ConfigureAwait(false))
            {
                Logger.Write(LogLevel.Debug, () => $"Stream [{streamName}] is frozen - waiting");
                Thread.Sleep(100);
            }
            Logger.Write(LogLevel.Debug, () => $"Stream [{streamName}] not in cache - reading from store");

            var events = await _store.GetEvents(streamName, start: snapshot?.Version + 1).ConfigureAwait(false);

            var eventstream = new EventStream<T>(Builder, this, bucket, streamId, events, snapshot);
            if (_shouldCache)
                await Cache<T>(eventstream).ConfigureAwait(false);

            return eventstream;
        }

        public Task<IEventStream> NewStream<T>(string bucket, string streamId) where T : class, IEventSource
        {
            Logger.Write(LogLevel.Debug, () => $"Creating new stream [{streamId}] in bucket [{bucket}]");
            IEventStream stream = new EventStream<T>(Builder, this, bucket, streamId, null, null);
            return Task.FromResult(stream);
        }

        public Task<IEnumerable<IWritableEvent>> GetEvents<T>(string bucket, string streamId, int? start = null, int? count = null) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            return _store.GetEvents(streamName, start: start, count: count);
        }
        public Task<IEnumerable<IWritableEvent>> GetEventsBackwards<T>(string bucket, string streamId, int? start = null, int? count = null) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            return _store.GetEventsBackwards(streamName, start: start, count: count);
        }
        

        public async Task<Guid> WriteStream<T>(IEventStream stream, Guid startingEventId, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), stream.Bucket, stream.StreamId);

            if (await CheckFrozen<T>(stream.Bucket, stream.StreamId).ConfigureAwait(false))
                throw new FrozenException();

            // If we increment commit id instead of depending on a commit header, ES will do the concurrency check for us
            foreach (var uncommitted in stream.Uncommitted.Where(x => !x.EventId.HasValue))
            {
                uncommitted.EventId = startingEventId;
                startingEventId = startingEventId.Increment();
            }

            Saved.Mark();
            await _store.WriteEvents(streamName, stream.Uncommitted, commitHeaders, expectedVersion: stream.CommitVersion).ConfigureAwait(false);

            if (_shouldCache)
                await Cache<T>(stream).ConfigureAwait(false);

            return startingEventId;
        }

        public async Task VerifyVersion<T>(IEventStream stream)
            where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), stream.Bucket, stream.StreamId);

            var last = await _store.GetEventsBackwards(streamName, count: 1).ConfigureAwait(false);
            if (!last.Any())
                throw new StorageException($"Expected version {stream.CommitVersion} on stream [{streamName}] - but no stream found");
            if (last.First().Descriptor.Version != stream.CommitVersion)
                throw new StorageException(
                    $"Expected version {stream.CommitVersion} on stream [{streamName}] - but read {last.First().Descriptor.Version}");
        }

        public async Task Freeze<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Freezing stream [{streamName}]");
            try
            {
                await _store.WriteMetadata(streamName, frozen: true, owner: Defaults.Instance).ConfigureAwait(false);
            }
            catch (VersionException)
            {
                Logger.Write(LogLevel.Debug, () => $"Freeze: stream [{streamName}] someone froze before us");
                throw new FrozenException();
            }
        }

        public async Task Unfreeze<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Unfreezing stream [{streamName}]");

            try
            {
                await _store.WriteMetadata(streamName, frozen: false).ConfigureAwait(false);
            }
            catch (VersionException)
            {
                Logger.Write(LogLevel.Debug, () => $"Unfreeze: stream [{streamName}] is not frozen");
                return;
            }
            
        }
        private Task<bool> CheckFrozen<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            return _store.IsFrozen(streamName);
        }

    }
}
