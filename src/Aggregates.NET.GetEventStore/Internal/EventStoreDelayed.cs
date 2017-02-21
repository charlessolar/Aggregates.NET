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

        private static Task _flusher;

        //                                                Channel  key          Last Pull   Lock    Stored Objects
        private static readonly ConcurrentDictionary<Tuple<string, string>, Tuple<DateTime, object, List<object>>> MemCache = new ConcurrentDictionary<Tuple<string, string>, Tuple<DateTime, object, List<object>>>();
        private static int _memCacheTotalSize = 0;
        private static int _tooLarge = 0;

        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _streamGen;
        private readonly string _endpoint;

        private readonly object _lock = new object();
        private Dictionary<Tuple<string, string>, List<object>> _inFlightMemCache;
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

                // A list of channels who have expired or have more than 1/10 the max total cache size
                var expiredSpecificChannels =
                    MemCache.Where(x => all || (DateTime.UtcNow - x.Value.Item1) > flushState.Expiration || (x.Value.Item3.Count > (_memCacheTotalSize / 10)))
                        .Select(x => x.Key).Take(MemCache.Keys.Count / 5)
                        .ToList();

                await expiredSpecificChannels.SelectAsync(async (expired) =>
                {
                    Tuple<DateTime, object, List<object>> fromCache;
                    if (!MemCache.TryRemove(expired, out fromCache))
                        return;

                    Logger.Write(LogLevel.Info,
                        () => $"Flushing expired channel {expired.Item1} key {expired.Item2} with {fromCache.Item3.Count} objects");

                    // Just take 500 from the end of the channel to prevent trying to write 20,000 items in 1 go
                    List<object> overLimit;
                    lock (fromCache.Item2)
                    {
                        overLimit =
                            fromCache.Item3.GetRange(Math.Max(0, fromCache.Item3.Count - flushState.ReadSize),
                                Math.Min(fromCache.Item3.Count, flushState.ReadSize)).ToList();
                        fromCache.Item3.RemoveRange(Math.Max(0, fromCache.Item3.Count - flushState.ReadSize),
                            Math.Min(fromCache.Item3.Count, flushState.ReadSize));
                    }

                    if (fromCache.Item3.Any())
                    {
                        // Put rest back in cache
                        MemCache.AddOrUpdate(expired,
                            (key) =>
                                    new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, fromCache.Item2, fromCache.Item3),
                            (key, existing) =>
                            {
                                lock (existing.Item2) existing.Item3.AddRange(fromCache.Item3);
                                return new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, existing.Item2, existing.Item3);
                            }
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
                        // Take lock on list lock so no new objects can be added while we flush
                        // prevents the consumer from adding items to cache faster than we can possibly flush
                        Monitor.Enter(fromCache.Item2);

                        // Stream name to contain the channel, specific key, and the instance id
                        // it doesn't matter whats in the streamname, the category projection will queue it for execution anyway
                        // and a lot of writers to a single stream makes eventstore slow
                        var streamName = flushState.StreamGen(typeof(EventStoreDelayed),
                            $"{flushState.Endpoint}.{StreamTypes.Delayed}", Assembly.GetEntryAssembly().FullName,
                            $"{expired.Item1}.{expired.Item2}.{Defaults.Instance}");
                        await flushState.Store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                        Flushes.Mark(expired.Item1);
                        MemCacheSize.Decrement(overLimit.Count);
                        Interlocked.Add(ref totalFlushed, overLimit.Count);
                        Interlocked.Add(ref _memCacheTotalSize, -overLimit.Count);
                    }
                    catch (Exception e)
                    {
                        Logger.Write(LogLevel.Warn,
                            () => $"Failed to write to channel [{expired.Item1}].  Exception: {e.GetType().Name}: {e.Message}");
                        // Failed to write to ES - put object back in memcache
                        MemCache.AddOrUpdate(expired,
                            (key) =>
                                new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, fromCache.Item2,
                                    overLimit),
                            (key, existing) =>
                            {
                                lock (existing.Item2) existing.Item3.AddRange(overLimit);
                                return new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, existing.Item2,
                                    existing.Item3);
                            }
                        );

                    }
                    finally
                    {
                        Monitor.Exit(fromCache.Item2);
                    }
                }).ConfigureAwait(false);

                if (ctx.Elapsed > TimeSpan.FromSeconds(10))
                    SlowLogger.Warn($"Flushing {totalFlushed} expired delayed objects took {ctx.Elapsed.TotalSeconds} seconds!");
                Logger.Write(LogLevel.Info, () => $"Flushing {totalFlushed} expired delayed objects took {ctx.Elapsed.TotalMilliseconds} ms");
                FlushedSize.Update(totalFlushed);
            }

            while (_memCacheTotalSize > flushState.MaxSize)
            {
                Logger.Write(LogLevel.Info,
                    () => $"Flushing too large delayed channels - cache size: {_memCacheTotalSize} - total channels: {MemCache.Keys.Count}");

                if (_memCacheTotalSize > (flushState.MaxSize*1.5))
                {
                    Logger.Write(LogLevel.Warn,
                        () => $"Delay cache has grown too large - pausing message processing while we flush!");
                    Interlocked.CompareExchange(ref _tooLarge, 1, 0);
                }

                using (var ctx = FlushedTime.NewContext())
                {
                    var totalFlushed = 0;
                    // Flush 500 off the oldest streams until total size is under limit or we've flushed all the streams
                    var toFlush = MemCache.OrderBy(x => x.Value.Item1).Select(x => x.Key).Take(MemCache.Keys.Count/5).ToList();

                    await toFlush.SelectAsync(async (expired) =>
                    {
                        Tuple<DateTime, object, List<object>> fromCache;
                        if (!MemCache.TryRemove(expired, out fromCache))
                            return;

                        var start = Math.Max(0, fromCache.Item3.Count - flushState.ReadSize);
                        var toTake = Math.Min(fromCache.Item3.Count, flushState.ReadSize);

                        // Special case if a channel is so large it begins to dominate the whole cache
                        if (fromCache.Item3.Count > (_memCacheTotalSize / 3))
                        {
                            start = fromCache.Item3.Count - (_memCacheTotalSize / 10);
                            toTake = (_memCacheTotalSize / 10);
                        }

                        List<object> overLimit;
                        lock (fromCache.Item2)
                        {
                            // Take from the end of the channel
                            overLimit = fromCache.Item3.GetRange(start, toTake).ToList();
                            fromCache.Item3.RemoveRange(start, toTake);
                        }

                        if (fromCache.Item3.Any())
                        {
                            // Put rest back in cache
                            MemCache.AddOrUpdate(expired,
                            (key) =>
                                new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, fromCache.Item2,
                                    fromCache.Item3),
                            (key, existing) =>
                            {
                                lock (existing.Item2) existing.Item3.AddRange(fromCache.Item3);
                                return new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, existing.Item2,
                                    existing.Item3);
                            }
                        );
                        }

                        Logger.Write(LogLevel.Info,
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
                            // Take lock on list lock so no new objects can be added while we flush
                            // prevents the consumer from adding items to cache faster than we can possibly flush
                            Monitor.Enter(fromCache.Item2);
                            var streamName = flushState.StreamGen(typeof(EventStoreDelayed),
                                $"{flushState.Endpoint}.{StreamTypes.Delayed}",
                                Assembly.GetEntryAssembly().FullName,
                                $"{expired.Item1}.{expired.Item2}.{Defaults.Instance}");
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
                                new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, fromCache.Item2,
                                    overLimit),
                            (key, existing) =>
                            {
                                lock (existing.Item2) existing.Item3.AddRange(overLimit);
                                return new Tuple<DateTime, object, List<object>>(DateTime.UtcNow,
                                    existing.Item2,
                                    existing.Item3);
                            }
                        );

                        }
                        finally
                        {
                            Monitor.Exit(fromCache.Item2);
                        }

                    }).ConfigureAwait(false);
                    FlushedSize.Update(totalFlushed);
                }

            }


        }

        public EventStoreDelayed(IStoreEvents store, string endpoint, int maxSize, int readSize, TimeSpan flushInterval, StreamIdGenerator streamGen)
        {
            _store = store;
            _streamGen = streamGen;
            _endpoint = endpoint;

            if (_flusher == null)
            {
                // Add a process exit event handler to flush cached delayed events before exiting the app
                // Not perfect in the case of a fatal app error - but something
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => Flush(new FlushState { Store = store, StreamGen = streamGen, Endpoint = endpoint, MaxSize = maxSize, ReadSize = readSize, Expiration = flushInterval }, all: true).Wait();
                _flusher = Timer.Repeat((s) => Flush(s), new FlushState { Store = store, StreamGen = streamGen, Endpoint = endpoint, MaxSize = maxSize, ReadSize = readSize, Expiration = flushInterval }, flushInterval, "delayed flusher");
            }
        }

        public Task Begin()
        {
            _uncommitted = new Dictionary<Tuple<string, string>, List<object>>();
            _inFlightMemCache = new Dictionary<Tuple<string, string>, List<object>>();
            return Task.CompletedTask;
        }

        public async Task End(Exception ex = null)
        {
            // If cache grows larger than 150% of max cache size, pause all processing until flush finished
            while (Interlocked.CompareExchange(ref _tooLarge, 1, 1) == 1)
                await Task.Delay(10).ConfigureAwait(false);

            if (ex != null)
            {
                Logger.Write(LogLevel.Info, () => $"Putting {_inFlightMemCache.Count()} in flight channels back into memcache");
                foreach (var inflight in _inFlightMemCache)
                {
                    MemCache.AddOrUpdate(inflight.Key,
                        (key) =>
                                new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, new object(), inflight.Value),
                        (key, existing) =>
                        {
                            lock (existing.Item2) inflight.Value.AddRange(existing.Item3);
                            return new Tuple<DateTime, object, List<object>>(existing.Item1, existing.Item2, inflight.Value);
                        }
                    );
                    MemCacheSize.Increment(inflight.Value.Count);
                    Interlocked.Add(ref _memCacheTotalSize, inflight.Value.Count);
                }
            }

            if (ex == null)
            {
                Logger.Write(LogLevel.Info, () => $"Putting {_uncommitted.Count()} delayed streams into mem cache");

                _inFlightMemCache.Clear();

                await _uncommitted.WhileAsync(async kv =>
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
                            var streamName = _streamGen(typeof(EventStoreDelayed),
                                $"{_endpoint}.{StreamTypes.Delayed}",
                                Assembly.GetEntryAssembly().FullName, kv.Key.Item1);
                            await _store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                            return;
                        }
                        catch (Exception e)
                        {
                            Logger.Write(LogLevel.Warn,
                                () => $"Failed to write to channel [{kv.Key.Item1}].  Exception: {e.GetType().Name}: {e.Message}");
                        }
                    }


                    // Failed to write to ES or has specific key - put objects into memcache
                    MemCache.AddOrUpdate(kv.Key,
                        (key) =>
                                new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, new object(), kv.Value),
                        (key, existing) =>
                        {
                            lock (existing.Item2) existing.Item3.AddRange(kv.Value);
                            return new Tuple<DateTime, object, List<object>>(existing.Item1, existing.Item2, existing.Item3);
                        }
                    );
                    MemCacheSize.Increment(kv.Value.Count);
                    Interlocked.Add(ref _memCacheTotalSize, kv.Value.Count);

                }).ConfigureAwait(false);
            }
        }

        public Task<TimeSpan?> Age(string channel, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}] key [{key}]");

            var specificAge = TimeSpan.Zero;

            if (!string.IsNullOrEmpty(key))
            {

                // Get age from memcache
                var specificKey = new Tuple<string, string>(channel, key);
                Tuple<DateTime, object, List<object>> temp;
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
                Tuple<DateTime, object, List<object>> temp;
                if (MemCache.TryGetValue(specificKey, out temp))
                    specificSize = temp.Item3.Count;

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

            Logger.Write(LogLevel.Info, () => $"Pulling delayed channel [{channel}] key [{key}]");

            Tuple<DateTime, object, List<object>> fromCache;

            // Check memcache even if key == null because messages failing to save to ES are put in memcache
            if (!MemCache.TryRemove(specificKey, out fromCache))
                fromCache = null;

            List<object> discovered = new List<object>();
            if (fromCache != null)
            {
                lock (fromCache.Item2)
                {
                    discovered = fromCache.Item3.GetRange(0, Math.Min(max ?? int.MaxValue, fromCache.Item3.Count)).ToList();
                    if (max.HasValue)
                        fromCache.Item3.RemoveRange(0, Math.Min(max.Value, fromCache.Item3.Count));
                    else
                        fromCache.Item3.Clear();
                }
            }

            lock (_lock) _inFlightMemCache.Add(specificKey, discovered);


            // Add back into memcache if Max was used and some elements remain
            if (fromCache != null && fromCache.Item3.Any())
            {
                MemCache.AddOrUpdate(specificKey,
                    (_) =>
                            new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, fromCache.Item2, fromCache.Item3),
                    (_, existing) =>
                    {
                        lock (existing.Item2) fromCache.Item3.AddRange(existing.Item3);
                        return new Tuple<DateTime, object, List<object>>(DateTime.UtcNow, existing.Item2, fromCache.Item3);
                    });
            }
            MemCacheSize.Decrement(discovered.Count);
            Interlocked.Add(ref _memCacheTotalSize, -discovered.Count);

            Logger.Write(LogLevel.Info, () => $"Pulled {discovered.Count} from delayed channel [{channel}] key [{key}]");
            return Task.FromResult<IEnumerable<object>>(discovered);
        }
        

    }
}
