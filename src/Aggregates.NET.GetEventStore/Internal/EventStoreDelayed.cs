using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Metrics.Utils;
using Newtonsoft.Json;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using Timer = System.Threading.Timer;

namespace Aggregates.Internal
{
    class EventStoreDelayed : IDelayedChannel, ILastApplicationUnitOfWork
    {
        public IBuilder Builder { get; set; }
        // The number of times the event has been re-run due to error
        public int Retries { get; set; }
        // Will be persisted across retries
        public ContextBag Bag { get; set; }

        private class InFlightInfo
        {
            public DateTime At { get; set; }
            public int Position { get; set; }
        }

        private class FlushState
        {
            public IStoreEvents Store { get; set; }
            public TimeSpan Expiration { get; set; }
            public StreamIdGenerator StreamGen { get; set; }
            public string Endpoint { get; set; }
            public int MaxSize { get; set; }
            public int ReadSize { get; set; }
        }

        private static readonly Counter MemCacheSize = Metric.Counter("Delayed Cache Size", Unit.Items);
        private static readonly Histogram DelayedSize = Metric.Histogram("Delayed Channel Size", Unit.Items, tags: "debug");
        private static readonly Histogram DelayedAge = Metric.Histogram("Delayed Channel Age", Unit.Items, tags: "debug");
        private static readonly Histogram FlushedSize = Metric.Histogram("Delayed Flushed", Unit.Items, tags: "debug");
        private static readonly Metrics.Timer FlushedTime = Metric.Timer("Delayed Flush Time", Unit.None, tags: "debug");
        private static readonly Meter Flushes = Metric.Meter("Delayed Flushs", Unit.Items, tags: "debug");

        private static readonly ILog Logger = LogManager.GetLogger("EventStoreDelayed");
        private static readonly ILog SlowLogger = LogManager.GetLogger("Slow Alarm");

        // If cache size gets too big lock this to prevent new items until completely flushed
        private static readonly SemaphoreSlim TooLargeLock = new SemaphoreSlim(1);
        private static Task _flusher;

        //                                                Channel  key          Last Pull   Stored Objects
        private static readonly ConcurrentDictionary<Tuple<string, string>, Tuple<DateTime, List<object>>> MemCache = new ConcurrentDictionary<Tuple<string, string>, Tuple<DateTime, List<object>>>();
        private static int _memCacheTotalSize = 0;

        private static readonly ConcurrentDictionary<string, Tuple<DateTime, object>> AgeSizeCache = new ConcurrentDictionary<string, Tuple<DateTime, object>>();


        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _streamGen;

        private readonly object _lock = new object();
        private Dictionary<Tuple<string, string>, List<object>> _inFlightMemCache;
        private Dictionary<string, InFlightInfo> _inFlightChannel;
        private Dictionary<Tuple<string, string>, List<object>> _uncommitted;

        static async Task Flush(object state, bool all = false)
        {
            var flushState = state as FlushState;
            if (all)
                Logger.Write(LogLevel.Info, () => $"App shutting down, flushing ALL mem cached channels");

            Logger.Write(LogLevel.Info, () => $"Flushing expired delayed channels - cache size: {_memCacheTotalSize} - total channels: {MemCache.Keys.Count}");
            using (var ctx = FlushedTime.NewContext())
            {
                var totalFlushed = 0;

                var expiredSpecificChannels =
                    MemCache.Where(x => all || (DateTime.UtcNow - x.Value.Item1) > flushState.Expiration)
                        .Select(x => x.Key).Take(10)
                        .ToList();

                await expiredSpecificChannels.SelectAsync(async (expired) =>
                {
                    Tuple<DateTime, List<object>> fromCache;
                    if (!MemCache.TryRemove(expired, out fromCache))
                        return;

                    Logger.Write(LogLevel.Debug,
                        () => $"Flushing expired channel {expired.Item1} key {expired.Item2} with {fromCache.Item2.Count} objects");

                    // Just take 500 from the end of the channel to prevent trying to write 20,000 items in 1 go
                    var overLimit =
                        fromCache.Item2.GetRange(Math.Max(0, fromCache.Item2.Count - flushState.ReadSize),
                            Math.Min(fromCache.Item2.Count, flushState.ReadSize)).ToList();
                    fromCache.Item2.RemoveRange(Math.Max(0, fromCache.Item2.Count - flushState.ReadSize),
                        Math.Min(fromCache.Item2.Count, flushState.ReadSize));

                    if (fromCache.Item2.Any())
                    {
                        // Put rest back in cache
                        MemCache.AddOrUpdate(expired,
                            (key) =>
                                    new Tuple<DateTime, List<object>>(DateTime.UtcNow, fromCache.Item2),
                            (key, existing) =>
                                new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                                    existing.Item2.Concat(fromCache.Item2).ToList())
                        );
                    }

                    var translatedEvents = overLimit.Select(x => new WritableEvent
                    {
                        Descriptor = new EventDescriptor
                        {
                            EntityType = "DELAY",
                            StreamType = $"{flushState.Endpoint}.{StreamTypes.Delayed}",
                            Bucket = Assembly.GetEntryAssembly().FullName,
                            StreamId = expired.Item1,
                            Timestamp = DateTime.UtcNow,
                            Headers = new Dictionary<string, string>()
                        },
                        Event = x,
                    });
                    try
                    {
                        var streamName = flushState.StreamGen(typeof(EventStoreDelayed),
                            $"{flushState.Endpoint}.{StreamTypes.Delayed}", Assembly.GetEntryAssembly().FullName,
                            expired.Item1);
                        await flushState.Store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                        Flushes.Mark(expired.Item1);
                        MemCacheSize.Decrement(-overLimit.Count);
                        Interlocked.Add(ref totalFlushed, overLimit.Count);
                        Interlocked.Add(ref _memCacheTotalSize, -overLimit.Count);
                    }
                    catch (Exception e)
                    {
                        Logger.Write(LogLevel.Warn,
                            () =>
                                    $"Failed to write to channel [{expired.Item1}].  Exception: {e.GetType().Name}: {e.Message}");
                        // Failed to write to ES - put object back in memcache
                        MemCache.AddOrUpdate(expired,
                            (key) =>
                                    new Tuple<DateTime, List<object>>(DateTime.UtcNow, fromCache.Item2),
                            (key, existing) =>
                                new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                                    existing.Item2.Concat(fromCache.Item2).ToList())
                        );

                    }
                }).ConfigureAwait(false);

                if (ctx.Elapsed > TimeSpan.FromSeconds(10))
                    SlowLogger.Warn($"Flushing {totalFlushed} expired delayed objects took {ctx.Elapsed.TotalSeconds} seconds!");
                FlushedSize.Update(totalFlushed);
            }

            while (_memCacheTotalSize > flushState.MaxSize)
            {
                await TooLargeLock.WaitAsync().ConfigureAwait(false);
                try
                {

                    Logger.Write(LogLevel.Info,
                        () => $"Flushing too large delayed channels - cache size: {_memCacheTotalSize} - total channels: {MemCache.Keys.Count}");

                    using (var ctx = FlushedTime.NewContext())
                    {
                        var totalFlushed = 0;
                        // Flush 500 off the oldest streams until total size is under limit or we've flushed all the streams
                        var toFlush = MemCache.OrderBy(x => x.Value.Item1).Select(x => x.Key).Take(10).ToList();

                        await toFlush.SelectAsync(async (expired) =>
                        {
                            Tuple<DateTime, List<object>> fromCache;
                            if (!MemCache.TryRemove(expired, out fromCache))
                                return;

                            // Take 500 from the end of the channel
                            var overLimit =
                                fromCache.Item2.GetRange(Math.Max(0, fromCache.Item2.Count - flushState.ReadSize),
                                    Math.Min(fromCache.Item2.Count, flushState.ReadSize)).ToList();
                            fromCache.Item2.RemoveRange(Math.Max(0, fromCache.Item2.Count - flushState.ReadSize),
                                Math.Min(fromCache.Item2.Count, flushState.ReadSize));

                            if (fromCache.Item2.Any())
                            {
                                // Put rest back in cache
                                MemCache.AddOrUpdate(expired,
                                    (key) =>
                                            new Tuple<DateTime, List<object>>(DateTime.UtcNow, fromCache.Item2),
                                    (key, existing) =>
                                        new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                                            existing.Item2.Concat(fromCache.Item2).ToList())
                                );
                            }

                            Logger.Write(LogLevel.Debug,
                                () => $"Flushing too large channel {expired.Item1} key {expired.Item2} with {overLimit.Count} objects");
                            var translatedEvents = overLimit.Select(x => new WritableEvent
                            {
                                Descriptor = new EventDescriptor
                                {
                                    EntityType = "DELAY",
                                    StreamType = $"{flushState.Endpoint}.{StreamTypes.Delayed}",
                                    Bucket = Assembly.GetEntryAssembly().FullName,
                                    StreamId = expired.Item1,
                                    Timestamp = DateTime.UtcNow,
                                    Headers = new Dictionary<string, string>()
                                },
                                Event = x,
                            });
                            try
                            {
                                var streamName = flushState.StreamGen(typeof(EventStoreDelayed),
                                    $"{flushState.Endpoint}.{StreamTypes.Delayed}", Assembly.GetEntryAssembly().FullName,
                                    expired.Item1);
                                await flushState.Store.WriteEvents(streamName, translatedEvents, null)
                                        .ConfigureAwait(false);
                                Flushes.Mark(expired.Item1);
                                MemCacheSize.Decrement(overLimit.Count);
                                Interlocked.Add(ref totalFlushed, overLimit.Count);
                                Interlocked.Add(ref _memCacheTotalSize, -overLimit.Count);
                            }
                            catch (Exception e)
                            {
                                Logger.Write(LogLevel.Warn,
                                    () =>
                                            $"Failed to write to channel [{expired.Item1}].  Exception: {e.GetType().Name}: {e.Message}");
                                // Failed to write to ES - put object back in memcache
                                MemCache.AddOrUpdate(expired,
                                    (key) =>
                                            new Tuple<DateTime, List<object>>(DateTime.UtcNow, fromCache.Item2),
                                    (key, existing) =>
                                        new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                                            existing.Item2.Concat(fromCache.Item2).ToList())
                                );

                            }

                        }).ConfigureAwait(false);
                        FlushedSize.Update(totalFlushed);

                    }
                }
                finally
                {
                    TooLargeLock.Release();
                }
            }


        }

        public EventStoreDelayed(IStoreEvents store, string endpoint, int maxSize, int readSize, TimeSpan flushInterval, StreamIdGenerator streamGen)
        {
            _store = store;
            _streamGen = streamGen;

            if (_flusher == null)
            {
                // Add a process exit event handler to flush cached delayed events before exiting the app
                // Not perfect in the case of a fatal app error - but something
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => Flush(new FlushState { Store = store, StreamGen = streamGen, Endpoint = endpoint, MaxSize = maxSize, ReadSize = readSize, Expiration = flushInterval }, all: true).Wait();
                _flusher = Timer.Repeat((s) => Flush(s), new FlushState { Store = store, StreamGen = streamGen, Endpoint = endpoint, MaxSize = maxSize, ReadSize=readSize, Expiration = flushInterval }, flushInterval, "delayed flusher");
            }
        }

        public Task Begin()
        {
            _uncommitted = new Dictionary<Tuple<string, string>, List<object>>();
            _inFlightChannel = new Dictionary<string, InFlightInfo>();
            _inFlightMemCache = new Dictionary<Tuple<string, string>, List<object>>();
            return Task.CompletedTask;
        }

        public async Task End(Exception ex = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Saving {_inFlightChannel.Count()} {(ex == null ? "ACKs" : "NACKs")}");
            await _inFlightChannel.ToList().SelectAsync(x => ex == null ? Ack(x.Key) : NAck(x.Key)).ConfigureAwait(false);

            if (ex != null)
            {
                Logger.Write(LogLevel.Debug, () => $"Putting {_inFlightMemCache.Count()} in flight channels back into memcache");
                foreach (var inflight in _inFlightMemCache)
                {
                    MemCache.AddOrUpdate(inflight.Key,
                        (key) =>
                                new Tuple<DateTime, List<object>>(DateTime.UtcNow, inflight.Value),
                        (key, existing) =>
                            new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                                existing.Item2.Concat(inflight.Value).ToList())
                    );
                    MemCacheSize.Increment(inflight.Value.Count);
                    Interlocked.Add(ref _memCacheTotalSize, inflight.Value.Count);
                }
            }

            if (ex == null)
            {
                Logger.Write(LogLevel.Debug, () => $"Putting {_uncommitted.Count()} delayed streams into mem cache");

                _inFlightMemCache.Clear();
                // Anything with a specific key goes into memcache
                foreach (var kv in _uncommitted.Where(x => !string.IsNullOrEmpty(x.Key.Item2)))
                {
                    MemCache.AddOrUpdate(kv.Key,
                        (key) =>
                            new Tuple<DateTime, List<object>>(DateTime.UtcNow, kv.Value),
                        (key, existing) =>
                            new Tuple<DateTime, List<object>>(DateTime.UtcNow, existing.Item2.Concat(kv.Value).ToList())
                        );
                    MemCacheSize.Increment(kv.Value.Count);
                    Interlocked.Add(ref _memCacheTotalSize, kv.Value.Count);
                }

                // Will block if currently flushing a large cache
                await TooLargeLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await _uncommitted.SelectAsync(async kv =>
                    {
                        // Anything without specific key gets committed to ES right away
                        if (string.IsNullOrEmpty(kv.Key.Item2))
                        {
                            var translatedEvents = kv.Value.Select(x => new WritableEvent
                            {
                                Descriptor = new EventDescriptor
                                {
                                    EntityType = "DELAY",
                                    StreamType = StreamTypes.Delayed,
                                    Bucket = Assembly.GetEntryAssembly().FullName,
                                    StreamId = kv.Key.Item1,
                                    Timestamp = DateTime.UtcNow,
                                    Headers = new Dictionary<string, string>()
                                },
                                Event = x,
                            });
                            try
                            {
                                var streamName = _streamGen(typeof(EventStoreDelayed), StreamTypes.Delayed,
                                    Assembly.GetEntryAssembly().FullName, kv.Key.Item1);
                                await _store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                                return;
                            }
                            catch (Exception e)
                            {
                                Logger.Write(LogLevel.Warn,
                                    () =>
                                            $"Failed to write to channel [{kv.Key.Item1}].  Exception: {e.GetType().Name}: {e.Message}");
                            }
                        }

                        // Failed to write to ES or has specific key - put objects into memcache
                        MemCache.AddOrUpdate(kv.Key,
                            (key) =>
                                    new Tuple<DateTime, List<object>>(DateTime.UtcNow, kv.Value),
                            (key, existing) =>
                                new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                                    existing.Item2.Concat(kv.Value).ToList())
                        );
                        MemCacheSize.Increment(kv.Value.Count);
                        Interlocked.Add(ref _memCacheTotalSize, kv.Value.Count);


                    }).ConfigureAwait(false);
                }
                finally
                {
                    TooLargeLock.Release();
                }
            }
        }

        private async Task<TimeSpan?> ChannelAge(string channel)
        {
            var streamName = _streamGen(typeof(EventStoreDelayed), StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);

            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}]");

            // Try cache
            Tuple<DateTime, object> cached;
            if (AgeSizeCache.TryGetValue($"{streamName}.age", out cached) && (DateTime.UtcNow - cached.Item1).TotalSeconds < 5)
            {
                Logger.Write(LogLevel.Debug, () => $"Got age from cache for channel [{channel}]");
                // null means we cached a negative result
                if (cached.Item2 == null)
                    return null;
                return (TimeSpan)cached.Item2;
            }

            // Try metadata
            try
            {
                var metadata = await _store.GetMetadata(streamName, "At").ConfigureAwait(false);
                if (!string.IsNullOrEmpty(metadata))
                {
                    var at = int.Parse(metadata);

                    Logger.Write(LogLevel.Debug, () => $"Got age from metadata of delayed channel [{channel}]");
                    var age = TimeSpan.FromSeconds(DateTime.UtcNow.ToUnixTime() - at);
                    AgeSizeCache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, age);

                    return age;
                }
            }
            catch (FrozenException)
            {
                // Someone else is processing
                AgeSizeCache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
                Logger.Write(LogLevel.Debug, () => $"Age is unavailable from delayed channel [{channel}] - stream frozen");
                return null;
            }

            // Try from first event in stream
            try
            {
                var firstEvent = await _store.GetEvents(streamName, StreamPosition.Start, 1).ConfigureAwait(false);
                if (firstEvent != null && firstEvent.Any())
                {

                    Logger.Write(LogLevel.Debug, () => $"Got age from first event of delayed channel [{channel}]");
                    var age = DateTime.UtcNow - firstEvent.Single().Descriptor.Timestamp;
                    AgeSizeCache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, age);

                    return age;
                }
            }
            catch (NotFoundException) { }

            // failed to get age from store, store a negative result
            AgeSizeCache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
            Logger.Write(LogLevel.Debug, () => $"Failed to get age of delayed channel [{channel}]");
            return null;
        }

        public Task<TimeSpan?> Age(string channel, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}] key [{key}]");

            var specificAge = TimeSpan.Zero;

            if (!string.IsNullOrEmpty(key))
            {

                // Get age from memcache
                var specificKey = new Tuple<string, string>(channel, key);
                Tuple<DateTime, List<object>> temp;
                if (MemCache.TryGetValue(specificKey, out temp))
                    specificAge = DateTime.UtcNow - temp.Item1;

            }
            DelayedAge.Update(Convert.ToInt64(specificAge.TotalSeconds));
            if (specificAge > TimeSpan.FromMinutes(30))
                SlowLogger.Write(LogLevel.Warn, () => $"Delayed channel [{channel}] specific [{key}] is {specificAge.TotalMinutes} minutes old!");

            return Task.FromResult<TimeSpan?>(specificAge);

        }

        public Task<int> Size(string channel, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}] key [{key}]");

            var specificSize = 0;
            if (!string.IsNullOrEmpty(key))
            {
                // Get size from memcache
                var specificKey = new Tuple<string, string>(channel, key);
                Tuple<DateTime, List<object>> temp;
                if (MemCache.TryGetValue(specificKey, out temp))
                    specificSize = temp.Item2.Count;

            }
            if (specificSize > 5000)
                SlowLogger.Write(LogLevel.Warn, () => $"Delayed channel [{channel}] specific [{key}] size is {specificSize}!");
            DelayedSize.Update(specificSize);
            return Task.FromResult(specificSize);
        }

        public Task AddToQueue(string channel, object queued, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Appending delayed object to channel [{channel}] key [{key}]");


            var specificKey = new Tuple<string, string>(channel, key);
            lock (_lock)
            {
                if (!_uncommitted.ContainsKey(specificKey))
                    _uncommitted[specificKey] = new List<object>();
                _uncommitted[specificKey].Add(queued);
            }


            return Task.CompletedTask;
        }

        public Task<IEnumerable<object>> Pull(string channel, string key = null, int? max = null)
        {
            var specificKey = new Tuple<string, string>(channel, key);

            Logger.Write(LogLevel.Debug, () => $"Pulling delayed channel [{channel}] key [{key}]");

            Tuple<DateTime, List<object>> fromCache;

            // Check memcache even if key == null because messages failing to save to ES are put in memcache
            if (!MemCache.TryRemove(specificKey, out fromCache))
                fromCache = null;

            var discovered = fromCache?.Item2.GetRange(0, Math.Min(max ?? int.MaxValue, fromCache.Item2.Count)).ToList() ?? new List<object>();
            lock (_lock) _inFlightMemCache.Add(specificKey, discovered);

            if (max.HasValue)
                fromCache?.Item2.RemoveRange(0, Math.Min(max.Value, fromCache.Item2.Count));
            else
                fromCache?.Item2.Clear();

            // Add back into memcache if Max was used and some elements remain
            if (fromCache != null && fromCache.Item2.Any())
            {
                MemCache.AddOrUpdate(specificKey,
                    (_) =>
                            new Tuple<DateTime, List<object>>(DateTime.UtcNow, fromCache.Item2),
                    (_, existing) =>
                        new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                            existing.Item2.Concat(fromCache.Item2).ToList()));
            }
            MemCacheSize.Decrement(discovered.Count);
            Interlocked.Add(ref _memCacheTotalSize, -discovered.Count);

            Logger.Write(LogLevel.Debug, () => $"Pulled {discovered.Count} from delayed channel [{channel}] key [{key}]");
            return Task.FromResult<IEnumerable<object>>(discovered);
        }

        private async Task Ack(string channel)
        {
            InFlightInfo info;
            lock (_lock)
            {
                if (!_inFlightChannel.ContainsKey(channel))
                    return;
                info = _inFlightChannel[channel];
                _inFlightChannel.Remove(channel);
            }

            Logger.Write(LogLevel.Debug, () => $"Acking channel [{channel}] position {info.Position} at {info.At.ToUnixTime()}");

            var streamName = _streamGen(typeof(EventStoreDelayed), StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);

            try
            {
                await _store.WriteMetadata(streamName, truncateBefore: info.Position, frozen: false, custom: new Dictionary<string, string>
                {
                    ["Position"] = info.Position.ToString(),
                    ["At"] = info.At.ToUnixTime().ToString()
                }).ConfigureAwait(false);
            }
            catch (VersionException)
            {
                Logger.Write(LogLevel.Error, () => $"Failed to save updated metadata for channel [{channel}] (should not happen)");
                throw;
            }
        }

        private async Task NAck(string channel)
        {
            lock (_lock)
            {
                if (!_inFlightChannel.ContainsKey(channel))
                    return;
            }
            Logger.Write(LogLevel.Debug, () => $"NAcking channel [{channel}]");

            // Remove the freeze so someone else can run the delayed
            var streamName = _streamGen(typeof(EventStoreDelayed), StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);

            lock (_lock) _inFlightChannel.Remove(channel);
            // Failed to process messages, unfreeze stream
            await _store.WriteMetadata(streamName, frozen: false).ConfigureAwait(false);
        }

    }
}
