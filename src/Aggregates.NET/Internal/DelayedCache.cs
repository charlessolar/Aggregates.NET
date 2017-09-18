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

        //                                          Channel  key          Last Pull   Stored Objects
        private readonly ConcurrentDictionary<Tuple<string, string>, Tuple<DateTime, List<IDelayedMessage>>> _memCache;

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
            _memCache = new ConcurrentDictionary<Tuple<string, string>, Tuple<DateTime, List<IDelayedMessage>>>();

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

            var cacheKey = new Tuple<string, string>(channel, key);
            _memCache.AddOrUpdate(cacheKey,
                           (k) =>
                                   new Tuple<DateTime, List<IDelayedMessage>>(DateTime.UtcNow, messages.ToList()),
                           (k, existing) =>
                           {
                               existing.Item2.AddRange(messages);

                               return new Tuple<DateTime, List<IDelayedMessage>>(DateTime.UtcNow, existing.Item2);
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
            var specificKey = new Tuple<string, string>(channel, key);
            Tuple<DateTime, List<IDelayedMessage>> temp;
            if (_memCache.TryGetValue(specificKey, out temp))
                specificAge = DateTime.UtcNow - temp.Item1;

            Tuple<DateTime, List<IDelayedMessage>> temp2;
            var channelKey = new Tuple<string, string>(channel, null);
            if (_memCache.TryGetValue(channelKey, out temp2) && (DateTime.UtcNow - temp2.Item1) > specificAge)
                specificAge = DateTime.UtcNow - temp2.Item1;

            return Task.FromResult<TimeSpan?>(specificAge);
        }

        public Task<int> Size(string channel, string key)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}] key [{key}]");

            var specificSize = 0;
            // Get size from memcache
            var specificKey = new Tuple<string, string>(channel, key);
            Tuple<DateTime, List<IDelayedMessage>> temp;
            if (_memCache.TryGetValue(specificKey, out temp))
                specificSize = temp.Item2.Count;

            Tuple<DateTime, List<IDelayedMessage>> temp2;
            var channelKey = new Tuple<string, string>(channel, null);
            if (_memCache.TryGetValue(channelKey, out temp2))
                specificSize += temp2.Item2.Count;

            return Task.FromResult(specificSize);
        }

        private IDelayedMessage[] pullFromMemCache(string channel, string key = null, int? max = null)
        {
            var cacheKey = new Tuple<string, string>(channel, key);

            Tuple<DateTime, List<IDelayedMessage>> fromCache;
            // Check memcache even if key == null because messages failing to save to ES are put in memcache
            if (!_memCache.TryRemove(cacheKey, out fromCache))
                fromCache = null;

            List<IDelayedMessage> discovered = new List<IDelayedMessage>();
            if (fromCache != null)
            {
                discovered = fromCache.Item2.GetRange(0, Math.Min(max ?? int.MaxValue, fromCache.Item2.Count)).ToList();
                if (max.HasValue)
                    fromCache.Item2.RemoveRange(0, Math.Min(max.Value, fromCache.Item2.Count));
                else
                    fromCache.Item2.Clear();
            }
            // Add back into memcache if Max was used and some elements remain
            if (fromCache != null && fromCache.Item2.Any())
            {
                _memCache.AddOrUpdate(cacheKey,
                    (_) =>
                            new Tuple<DateTime, List<IDelayedMessage>>(DateTime.UtcNow, fromCache.Item2),
                    (_, existing) =>
                    {
                        fromCache.Item2.AddRange(existing.Item2);

                        return new Tuple<DateTime, List<IDelayedMessage>>(DateTime.UtcNow, fromCache.Item2);
                    });
            }
            return discovered.ToArray();
        }

        private async Task Flush()
        {
            var memCacheTotalSize = _memCache.Values.Sum(x => x.Item2.Count);
            _metrics.Update("Delayed Cache Size", Unit.Items, memCacheTotalSize);

            Logger.Write(LogLevel.Info,
                () => $"Flushing expired delayed channels - cache size: {memCacheTotalSize} - total channels: {_memCache.Keys.Count}");

            var totalFlushed = 0;

            // A list of channels who have expired or have more than 1/5 the max total cache size
            var expiredSpecificChannels =
                _memCache.Where(x => (DateTime.UtcNow - x.Value.Item1) > _expiration)
                    .Select(x => x.Key).Take(Math.Max(1, _memCache.Keys.Count / 5))
                    .ToArray();

            await expiredSpecificChannels.StartEachAsync(3, async (expired) =>
            {
                var messages = pullFromMemCache(expired.Item1, expired.Item2, max: _flushSize);

                if (!messages.Any())
                    return;

                Logger.Write(LogLevel.Info,
                    () => $"Flushing {messages.Length} expired messages from channel {expired.Item1} key {expired.Item2}");

                var translatedEvents = messages.Select(x => (IFullEvent)new FullEvent
                {
                    Descriptor = new EventDescriptor
                    {
                        EntityType = "DELAY",
                        StreamType = $"{_endpoint}.{StreamTypes.Delayed}",
                        Bucket = Assembly.GetEntryAssembly()?.FullName ?? "UNKNOWN",
                        StreamId = expired.Item1,
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
                                $"{expired.Item1}.{expired.Item2}", new Id[] { });
                    await _store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                    Interlocked.Add(ref totalFlushed, messages.Length);
                    Interlocked.Add(ref memCacheTotalSize, -messages.Length);

                }
                catch (Exception e)
                {
                    Logger.Write(LogLevel.Warn,
                        () => $"Failed to write to channel [{expired.Item1}].  Exception: {e.GetType().Name}: {e.Message}");

                            // Failed to write to ES - put object back in memcache
                            _memCache.AddOrUpdate(expired,
                                (key) =>
                                    new Tuple<DateTime, List<IDelayedMessage>>(DateTime.UtcNow, messages.ToList()),
                                (key, existing) =>
                                {
                                    existing.Item2.AddRange(messages);

                                    return new Tuple<DateTime, List<IDelayedMessage>>(DateTime.UtcNow, existing.Item2);
                                });

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
                    var toFlush = _memCache.Where(x => x.Value.Item2.Count > _flushSize || (x.Value.Item2.Count > (_maxSize / 5))).Select(x => x.Key).Take(Math.Max(1, _memCache.Keys.Count / 5)).ToArray();
                    // If no large channels, take some of the oldest
                    if (!toFlush.Any())
                        toFlush = _memCache.OrderBy(x => x.Value.Item1).Select(x => x.Key).Take(Math.Max(1, _memCache.Keys.Count / 5)).ToArray();

                    await toFlush.StartEachAsync(3, async (expired) =>
                    {
                        var messages = pullFromMemCache(expired.Item1, expired.Item2, max: _flushSize);

                        Logger.Write(LogLevel.Info,
                            () => $"Flushing {messages.Length} messages from large channel {expired.Item1} key {expired.Item2}");
                        var translatedEvents = messages.Select(x => (IFullEvent)new FullEvent
                        {
                            Descriptor = new EventDescriptor
                            {
                                EntityType = "DELAY",
                                StreamType = $"{_endpoint}.{StreamTypes.Delayed}",
                                Bucket = Assembly.GetEntryAssembly()?.FullName ?? "UNKNOWN",
                                StreamId = expired.Item1,
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
                            $"{expired.Item1}.{expired.Item2}", new Id[] { });

                            await _store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                            Interlocked.Add(ref totalFlushed, messages.Length);
                            Interlocked.Add(ref memCacheTotalSize, -messages.Length);
                        }
                        catch (Exception e)
                        {
                            limit--;
                            Logger.Write(LogLevel.Warn,
                                () => $"Failed to write to channel [{expired.Item1}].  Exception: {e.GetType().Name}: {e.Message}");

                            // Failed to write to ES - put object back in memcache
                            _memCache.AddOrUpdate(expired,
                                (key) =>
                                    new Tuple<DateTime, List<IDelayedMessage>>(DateTime.UtcNow, messages.ToList()),
                                (key, existing) =>
                                {
                                    existing.Item2.AddRange(messages);

                                    return new Tuple<DateTime, List<IDelayedMessage>>(DateTime.UtcNow, existing.Item2);
                                });
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
