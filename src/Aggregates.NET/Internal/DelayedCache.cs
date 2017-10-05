using Aggregates.Contracts;
using Aggregates.Logging;
using Aggregates.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{

    class DelayedCache : IDelayedCache, IDisposable
    {
        class CacheKey
        {
            public CacheKey(string channel, string key)
            {
                Channel = channel;
                Key = key;
            }
            public string Channel { get; set; }
            public string Key { get; set; }

            public class EqualityComparer : IEqualityComparer<CacheKey>
            {

                public bool Equals(CacheKey x, CacheKey y)
                {
                    return x.Channel == y.Channel && x.Key == y.Key;
                }

                public int GetHashCode(CacheKey x)
                {
                    return (x?.Channel?.GetHashCode() ?? 1) ^ (x?.Key?.GetHashCode() ?? 1);
                }

            }
        }
        class CachedList
        {
            public long Created { get; set; }
            public long Modified { get; set; }
            public object Lock { get; set; }
            public Queue<IDelayedMessage> Messages { get; set; }
        }

        private static readonly ILog Logger = LogProvider.GetLogger("DelayedCache");


        private readonly IMetrics _metrics;
        private readonly IStoreEvents _store;
        private readonly TimeSpan _flushInterval;
        private readonly string _endpoint;
        private readonly int _maxSize;
        private readonly int _flushSize;
        private readonly TimeSpan _expiration;
        private readonly StreamIdGenerator _streamGen;
        private readonly Thread _thread;
        private readonly CancellationTokenSource _cts;


        private readonly ConcurrentDictionary<CacheKey, CachedList> _memCache;

        private int _tooLarge = 0;
        private bool _disposed;

        public DelayedCache(IMetrics metrics, IStoreEvents store, TimeSpan flushInterval, string endpoint, int maxSize, int flushSize, TimeSpan delayedExpiration, StreamIdGenerator streamGen)
        {
            _metrics = metrics;
            _store = store;
            _flushInterval = flushInterval;
            _endpoint = endpoint;
            _maxSize = maxSize;
            _flushSize = flushSize;
            _expiration = delayedExpiration;
            _streamGen = streamGen;
            _cts = new CancellationTokenSource();
            _memCache = new ConcurrentDictionary<CacheKey, CachedList>(new CacheKey.EqualityComparer());

            _thread = new Thread(Threaded)
            { IsBackground = true, Name = $"Delayed Cache Thread" };

            // Add a process exit event handler to flush cached delayed events before exiting the app
            // Not perfect in the case of a fatal app error - but something
            AppDomain.CurrentDomain.ProcessExit += (sender, e) => Threaded(this);
            _thread.Start(this);
        }

        private static void Threaded(object state)
        {
            var cache = (DelayedCache)state;

            try
            {
                while (true)
                {
                    cache._cts.Token.ThrowIfCancellationRequested();

                    Thread.Sleep(cache._flushInterval);
                    cache.Flush().Wait();
                }
            }
            catch { }
        }


        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts.Cancel();
            _thread.Join();
        }

        public async Task Add(string channel, string key, IDelayedMessage[] messages)
        {
            // If cache grows larger than 150% of max cache size, pause all processing until flush finished
            while (Interlocked.CompareExchange(ref _tooLarge, 1, 1) == 1)
                await Task.Delay(50).ConfigureAwait(false);

            // Anything without a key bypasses memory cache
            if (string.IsNullOrEmpty(key))
            {
                var translatedEvents = messages.Select(x => (IFullEvent)new FullEvent
                {
                    Descriptor = new EventDescriptor
                    {
                        EntityType = "DELAY",
                        StreamType = StreamTypes.Delayed,
                        Bucket = Assembly.GetEntryAssembly()?.FullName ?? "UNKNOWN",
                        StreamId = channel,
                        Timestamp = DateTime.UtcNow,
                        Headers = new Dictionary<string, string>()
                    },
                    Event = x,
                }).ToArray();
                try
                {
                    var streamName = _streamGen(typeof(DelayedCache),
                        $"{_endpoint}.{StreamTypes.Delayed}",
                        Assembly.GetEntryAssembly()?.FullName ?? "UNKNOWN", channel, new Id[] { });
                    await _store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                    return;
                }
                catch (Exception e)
                {
                    Logger.Write(LogLevel.Warn,
                        () => $"Failed to write to channel [{channel}].  Exception: {e.GetType().Name}: {e.Message}");
                }
            }

            var cacheKey = new CacheKey(channel, key);
            _memCache.AddOrUpdate(cacheKey,
                           (k) => new CachedList { Created = DateTime.UtcNow.Ticks, Modified = DateTime.UtcNow.Ticks, Lock = new object(), Messages = new Queue<IDelayedMessage>(messages) },
                           (k, existing) =>
                           {
                               lock (existing.Lock)
                               {
                                   foreach (var m in messages)
                                       existing.Messages.Enqueue(m);
                               }
                               existing.Modified = DateTime.UtcNow.Ticks;

                               return existing;
                           }
                       );

        }


        public Task<IDelayedMessage[]> Pull(string channel, string key = null, int? max = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed channel [{channel}] key [{key}] max [{max}]");

            var discovered = pullFromMemCache(channel, key, max);

            // Check empty key store too
            if (!string.IsNullOrEmpty(key))
            {
                var nonSpecific = pullFromMemCache(channel, null, max);
                discovered = discovered.Concat(nonSpecific).ToArray();
            }

            Logger.Write(LogLevel.Info, () => $"Pulled {discovered.Length} from delayed channel [{channel}] key [{key}]");
            return Task.FromResult(discovered);
        }

        public Task<TimeSpan?> Age(string channel, string key)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}] key [{key}]");

            var specificAge = TimeSpan.Zero;

            // Get age from memcache
            var specificKey = new CacheKey(channel, key);
            CachedList temp;
            if (_memCache.TryGetValue(specificKey, out temp))
                specificAge = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - temp.Modified);

            CachedList temp2;
            var channelKey = new CacheKey(channel, null);
            if (_memCache.TryGetValue(channelKey, out temp2) && TimeSpan.FromTicks(DateTime.UtcNow.Ticks - temp2.Modified) > specificAge)
                specificAge = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - temp2.Modified);

            return Task.FromResult<TimeSpan?>(specificAge);
        }

        public Task<int> Size(string channel, string key)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}] key [{key}]");

            var specificSize = 0;
            // Get size from memcache
            var specificKey = new CacheKey(channel, key);
            CachedList temp;
            if (_memCache.TryGetValue(specificKey, out temp))
                specificSize = temp.Messages.Count;

            CachedList temp2;
            var channelKey = new CacheKey(channel, null);
            if (_memCache.TryGetValue(channelKey, out temp2))
                specificSize += temp2.Messages.Count;

            return Task.FromResult(specificSize);
        }

        private IDelayedMessage[] pullFromMemCache(string channel, string key = null, int? max = null)
        {
            var cacheKey = new CacheKey(channel, key);

            CachedList fromCache;
            // Check memcache even if key == null because messages failing to save to ES are put in memcache
            if (!_memCache.TryRemove(cacheKey, out fromCache))
                fromCache = null;

            var discovered = new LinkedList<IDelayedMessage>();
            if (fromCache != null)
            {
                lock (fromCache.Lock)
                {
                    for (var i = 0; i <= Math.Min(max ?? int.MaxValue, fromCache.Messages.Count); i++)
                        discovered.AddLast(fromCache.Messages.Dequeue());
                }
            }
            // Add back into memcache if Max was used and some elements remain
            if (fromCache != null && fromCache.Messages.Any())
            {
                fromCache.Modified = DateTime.UtcNow.Ticks;

                _memCache.AddOrUpdate(cacheKey,
                               (k) => fromCache,
                               (k, existing) =>
                               {
                                   lock (existing.Lock)
                                   {
                                       foreach (var m in fromCache.Messages)
                                           existing.Messages.Enqueue(m);
                                   }
                                   return existing;
                               }
                           );
            }
            return discovered.ToArray();
        }

        private async Task Flush()
        {
            var memCacheTotalSize = _memCache.Values.Sum(x => x.Messages.Count);
            _metrics.Update("Delayed Cache Size", Unit.Items, memCacheTotalSize);

            Logger.Write(LogLevel.Info,
                () => $"Flushing expired delayed channels - cache size: {memCacheTotalSize} - total channels: {_memCache.Keys.Count}");

            var totalFlushed = 0;

            // A list of channels who have expired or have more than 1/5 the max total cache size
            var expiredSpecificChannels =
                _memCache.Where(x => TimeSpan.FromTicks(DateTime.UtcNow.Ticks - x.Value.Modified) > _expiration)
                    .Select(x => x.Key).Take(Math.Max(1, _memCache.Keys.Count / 5))
                    .ToArray();

            await expiredSpecificChannels.StartEachAsync(3, async (expired) =>
            {
                var messages = pullFromMemCache(expired.Channel, expired.Key, max: _flushSize);

                if (!messages.Any())
                    return;

                Logger.Write(LogLevel.Info,
                    () => $"Flushing {messages.Length} expired messages from channel {expired.Channel} key {expired.Key}");

                var translatedEvents = messages.Select(x => (IFullEvent)new FullEvent
                {
                    Descriptor = new EventDescriptor
                    {
                        EntityType = "DELAY",
                        StreamType = $"{_endpoint}.{StreamTypes.Delayed}",
                        Bucket = Assembly.GetEntryAssembly()?.FullName ?? "UNKNOWN",
                        StreamId = $"{expired.Channel}.{expired.Key}",
                        Timestamp = DateTime.UtcNow,
                        Headers = new Dictionary<string, string>()
                        {
                            ["Expired"] = "true",
                            ["FlushTime"] = DateTime.UtcNow.ToString("s"),
                            ["Instance"] = Defaults.Instance.ToString(),
                            ["Machine"] = Environment.MachineName,
                        }
                    },
                    Event = x,
                }).ToArray();
                try
                {
                    // Todo: might be a good idea to have a lock here so while writing to eventstore no new events can pile up


                    // Stream name to contain the channel, specific key, and the instance id
                    // it doesn't matter whats in the streamname, the category projection will queue it for execution anyway
                    // and a lot of writers to a single stream makes eventstore slow
                    var streamName = _streamGen(typeof(DelayedCache),
                        $"{_endpoint}.{StreamTypes.Delayed}", Assembly.GetEntryAssembly()?.FullName ?? "UNKNOWN",
                        $"{expired.Channel}.{expired.Key}", new Id[] { });
                    await _store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                    Interlocked.Add(ref totalFlushed, messages.Length);
                    Interlocked.Add(ref memCacheTotalSize, -messages.Length);

                }
                catch (Exception e)
                {
                    Logger.Write(LogLevel.Warn,
                        () => $"Failed to write to channel [{expired.Channel}].  Exception: {e.GetType().Name}: {e.Message}");

                    // Failed to write to ES - put object back in memcache
                    await Add(expired.Channel, expired.Key, messages).ConfigureAwait(false);

                }
            }).ConfigureAwait(false);



            try
            {
                var limit = 10;
                while (memCacheTotalSize > _maxSize && limit > 0)
                {
                    Logger.Write(LogLevel.Info,
                        () => $"Flushing too large delayed channels - cache size: {memCacheTotalSize} - total channels: {_memCache.Keys.Count}");

                    if (memCacheTotalSize > (_maxSize * 1.5))
                    {
                        Logger.Write(LogLevel.Warn,
                            () => $"Delay cache has grown too large - pausing message processing while we flush!");
                        Interlocked.CompareExchange(ref _tooLarge, 1, 0);
                    }

                    // Flush the largest channels
                    var toFlush = _memCache.Where(x => x.Value.Messages.Count > _flushSize || (x.Value.Messages.Count > (_maxSize / 5))).Select(x => x.Key).Take(Math.Max(1, _memCache.Keys.Count / 5)).ToArray();
                    // If no large channels, take some of the oldest
                    if (!toFlush.Any())
                        toFlush = _memCache.OrderBy(x => x.Value.Modified).Select(x => x.Key).Take(Math.Max(1, _memCache.Keys.Count / 5)).ToArray();

                    await toFlush.StartEachAsync(3, async (expired) =>
                    {
                        var messages = pullFromMemCache(expired.Channel, expired.Key, max: _flushSize);

                        Logger.Write(LogLevel.Info,
                            () => $"Flushing {messages.Length} messages from large channel {expired.Channel} key {expired.Key}");
                        var translatedEvents = messages.Select(x => (IFullEvent)new FullEvent
                        {
                            Descriptor = new EventDescriptor
                            {
                                EntityType = "DELAY",
                                StreamType = $"{_endpoint}.{StreamTypes.Delayed}",
                                Bucket = Assembly.GetEntryAssembly()?.FullName ?? "UNKNOWN",
                                StreamId = $"{expired.Channel}.{expired.Key}",
                                Timestamp = DateTime.UtcNow,
                                Headers = new Dictionary<string, string>()
                                {
                                    ["Expired"] = "false",
                                    ["FlushTime"] = DateTime.UtcNow.ToString("s"),
                                    ["Instance"] = Defaults.Instance.ToString(),
                                    ["Machine"] = Environment.MachineName,
                                }
                            },
                            Event = x,
                        }).ToArray();
                        try
                        {
                            // Todo: might be a good idea to have a lock here so while writing to eventstore no new events can pile up

                            var streamName = _streamGen(typeof(DelayedCache),
                            $"{_endpoint}.{StreamTypes.Delayed}",
                            Assembly.GetEntryAssembly()?.FullName ?? "UNKNOWN",
                            $"{expired.Channel}.{expired.Key}", new Id[] { });

                            await _store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                            Interlocked.Add(ref totalFlushed, messages.Length);
                            Interlocked.Add(ref memCacheTotalSize, -messages.Length);
                        }
                        catch (Exception e)
                        {
                            limit--;
                            Logger.Write(LogLevel.Warn,
                                () => $"Failed to write to channel [{expired.Channel}].  Exception: {e.GetType().Name}: {e.Message}");

                            // Failed to write to ES - put object back in memcache
                            await Add(expired.Channel, expired.Key, messages).ConfigureAwait(false);
                            throw;
                        }


                    }).ConfigureAwait(false);


                }
            }
            catch (Exception e)
            {
                var stackTrace = string.Join("\n", (e.StackTrace?.Split('\n').Take(10) ?? new string[] { }).AsEnumerable());
                Logger.Write(LogLevel.Error,
                    () => $"Caught exception: {e.GetType().Name}: {e.Message} while flushing cache messages\nStack: {stackTrace}");
            }
            finally
            {
                Interlocked.CompareExchange(ref _tooLarge, 0, 1);
            }

            Logger.Write(LogLevel.Info, () => $"Flushed {totalFlushed} expired delayed objects");
        }
    }
}
