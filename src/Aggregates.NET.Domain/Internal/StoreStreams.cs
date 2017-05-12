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
        private static readonly Meter Saved = Metric.Meter("Saved Streams", Unit.Items, tags: "debug");
        private static readonly Meter HitMeter = Metric.Meter("Stream Cache Hits", Unit.Events, tags: "debug");
        private static readonly Meter MissMeter = Metric.Meter("Stream Cache Misses", Unit.Events, tags: "debug");

        private static readonly ILog Logger = LogManager.GetLogger("StoreStreams");
        private readonly IStoreEvents _store;
        private readonly IBuilder _builder;
        private readonly ICache _cache;
        private readonly bool _shouldCache;
        private readonly StreamIdGenerator _streamGen;
        

        public StoreStreams(IBuilder builder, IStoreEvents store, ICache cache, bool cacheStreams, StreamIdGenerator streamGen)
        {
            _store = store;
            _builder = builder;
            _cache = cache;
            _shouldCache = cacheStreams;
            _streamGen = streamGen;
        }

        public Task Evict<T>(IEventStream stream) where T : class, IEventSource
        {
            if (!_shouldCache) return Task.CompletedTask;

            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId, stream.Parents);
            _cache.Evict(streamName);
            return Task.CompletedTask;
        }
        public Task Cache<T>(IEventStream stream) where T : class, IEventSource
        {
            if (!_shouldCache) return Task.CompletedTask;

            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId, stream.Parents);
            _cache.Cache(streamName, stream.Clone());
            return Task.CompletedTask;
        }

        public async Task<IEventStream> GetStream<T>(string bucket, Id streamId, IEnumerable<Id> parents = null, ISnapshot snapshot = null) where T : class, IEventSource
        {
            parents = parents ?? new Id[] { };
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, bucket, streamId, parents);

            Logger.Write(LogLevel.Debug, () => $"Retreiving stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName}");

            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as IEventStream;
                if (cached != null)
                {
                    HitMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Found stream [{streamName}] in cache");
                    return new EventStream<T>(cached, _builder, this, snapshot);
                }
                MissMeter.Mark();
            }

            while (await CheckFrozen<T>(bucket,streamId, parents).ConfigureAwait(false))
            {
                Logger.Write(LogLevel.Info, () => $"Stream [{streamName}] is frozen - waiting");
                await Task.Delay(100).ConfigureAwait(false);
            }
            Logger.Write(LogLevel.Debug, () => $"Stream [{streamName}] not in cache - reading from store");

            var events = await _store.GetEvents(streamName, start: snapshot?.Version).ConfigureAwait(false);

            var eventstream = new EventStream<T>(_builder, this, StreamTypes.Domain, bucket, streamId, parents, streamName, events, snapshot);
            
            if(_shouldCache)
                await Cache<T>(eventstream).ConfigureAwait(false);

            Logger.Write(LogLevel.Debug, () => $"Stream [{streamName}] read - version is {eventstream.CommitVersion}");
            return eventstream;
        }

        public Task<IEventStream> NewStream<T>(string bucket, Id streamId, IEnumerable<Id> parents = null) where T : class, IEventSource
        {
            parents = parents ?? new Id[] { };
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, bucket, streamId, parents);
            Logger.Write(LogLevel.Debug, () => $"Creating new stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName}");
            IEventStream stream = new EventStream<T>(_builder, this, StreamTypes.Domain, bucket, streamId, parents, streamName, null, null);

            return Task.FromResult(stream);
        }

        public Task<IEnumerable<IFullEvent>> GetEvents<T>(string bucket, Id streamId, IEnumerable<Id> parents = null, long? start = null, int? count = null) where T : class, IEventSource
        {
            parents = parents ?? new Id[] { };
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, bucket, streamId, parents);
            return _store.GetEvents(streamName, start: start, count: count);
        }
        public Task<IEnumerable<IFullEvent>> GetEventsBackwards<T>(string bucket, Id streamId, IEnumerable<Id> parents = null, long? start = null, int? count = null) where T : class, IEventSource
        {
            parents = parents ?? new Id[] { };
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, bucket, streamId, parents);
            return _store.GetEventsBackwards(streamName, start: start, count: count);
        }
        


        public async Task WriteStream<T>(IEventStream stream, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId, stream.Parents);

            if (await CheckFrozen<T>(stream.Bucket, stream.StreamId, stream.Parents).ConfigureAwait(false))
                throw new FrozenException();

            Saved.Mark();
            await _store.WriteEvents(streamName, stream.Uncommitted, commitHeaders, expectedVersion: stream.CommitVersion).ConfigureAwait(false);
        }

        public async Task VerifyVersion<T>(IEventStream stream)
            where T : class, IEventSource
        {
            // New streams dont need verification
            if (stream.CommitVersion == -1) return;
            Logger.Write(LogLevel.Debug, () => $"Stream [{stream.StreamId}] in bucket [{stream.Bucket}] for type {typeof(T).FullName} verifying stream version {stream.CommitVersion}");

            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId, stream.Parents);

            var last = await _store.GetEventsBackwards(streamName, count: 1).ConfigureAwait(false);
            if (!last.Any())
                throw new VersionException($"Expected version {stream.CommitVersion} on stream [{streamName}] - but no stream found");
            if (last.First().Descriptor.Version != stream.CommitVersion)
            {
                if (last.First().Descriptor.Version < stream.CommitVersion)
                {
                    Logger.Write(LogLevel.Warn,
                        $"Stream [{streamName}] at the store is version {last.First().Descriptor.Version} - our stream is version {stream.CommitVersion} - which is weird");
                    Logger.Write(LogLevel.Warn, $"Stream [{streamName}] snapshot version is: {stream.Snapshot?.Version} - committed count is: {stream.Committed.Count()} - uncomitted is: {stream.Uncommitted.Count()}");
                }
                throw new VersionException(
                    $"Expected version {stream.CommitVersion} on stream [{streamName}] - but read {last.First().Descriptor.Version}");
            }
            Logger.Write(LogLevel.Debug, () => $"Verified version of stream [{stream.StreamId}] in bucket [{stream.Bucket}] for type {typeof(T).FullName}");
        }

        public async Task Freeze<T>(IEventStream stream) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId, stream.Parents);
            Logger.Write(LogLevel.Info, () => $"Freezing stream [{streamName}]");
            try
            {
                await _store.WriteMetadata(streamName, frozen: true, owner: Defaults.Instance).ConfigureAwait(false);
            }
            catch (VersionException)
            {
                Logger.Write(LogLevel.Info, () => $"Freeze: stream [{streamName}] someone froze before us");
                throw new FrozenException();
            }
        }

        public async Task Unfreeze<T>(IEventStream stream) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, stream.Bucket, stream.StreamId, stream.Parents);
            Logger.Write(LogLevel.Info, () => $"Unfreezing stream [{streamName}]");

            try
            {
                await _store.WriteMetadata(streamName, frozen: false).ConfigureAwait(false);
            }
            catch (VersionException e)
            {
                Logger.Write(LogLevel.Info, () => $"Unfreeze failed on stream [{streamName}].  Message: {e.Message}");
            }

        }
        private Task<bool> CheckFrozen<T>(string bucket, Id streamId, IEnumerable<Id> parents = null) where T : class, IEventSource
        {
            parents = parents ?? new Id[] { };
            var streamName = _streamGen(typeof(T), StreamTypes.Domain, bucket, streamId, parents);
            return _store.IsFrozen(streamName);
        }

    }
}
