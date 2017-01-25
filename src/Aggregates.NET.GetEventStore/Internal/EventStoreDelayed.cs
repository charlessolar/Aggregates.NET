using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
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
            public StreamIdGenerator StreamGen { get; set; }
        }
        private static readonly Histogram DelayedRead = Metric.Histogram("Delayed Channel Read", Unit.Items);
        private static readonly Histogram DelayedSize = Metric.Histogram("Delayed Channel Size", Unit.Items);
        private static readonly Histogram DelayedAge = Metric.Histogram("Delayed Channel Age", Unit.Items);
        private static readonly Counter MemCacheSize = Metric.Counter("Delayed Cache Size", Unit.Items);
        private static readonly ILog Logger = LogManager.GetLogger("EventStoreDelayed");
        private static readonly ILog SlowLogger = LogManager.GetLogger("Slow Alarm");

        private static Task _flusher;

        //                                                Channel  key          Last Pull   Stored Objects
        private static readonly ConcurrentDictionary<Tuple<string, string>, Tuple<DateTime, List<object>>> MemCache = new ConcurrentDictionary<Tuple<string, string>, Tuple<DateTime, List<object>>>();


        private static readonly ConcurrentDictionary<string, Tuple<DateTime, object>> AgeSizeCache = new ConcurrentDictionary<string, Tuple<DateTime, object>>();
        private static readonly Dictionary<string, DateTime> RecentlyPulled = new Dictionary<string, DateTime>();
        private static readonly object RecentLock = new object();

        private static readonly Task Expiring = Timer.Repeat(() =>
        {
            lock (RecentLock)
            {
                var expired = RecentlyPulled.Where(x => x.Value < DateTime.UtcNow).ToList();
                foreach (var e in expired)
                    RecentlyPulled.Remove(e.Key);
            }
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(2));


        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _streamGen;

        private readonly object _lock = new object();
        private Dictionary<Tuple<string, string>, List<object>> _inFlightMemCache;
        private Dictionary<string, InFlightInfo> _inFlightChannel;
        private Dictionary<Tuple<string, string>, List<object>> _uncommitted;

        static async Task Flush(object state, bool? all = null)
        {
            var flushState = state as FlushState;

            // Todo: make expiration configurable
            var expiredSpecificChannels = MemCache.Where(x => (DateTime.UtcNow - x.Value.Item1) > TimeSpan.FromMinutes(5)).Select(x => x.Key).ToList();

            await expiredSpecificChannels.SelectAsync(async (expired) =>
            {
                Tuple<DateTime, List<object>> fromCache;
                if (!MemCache.TryRemove(expired, out fromCache))
                    return;

                Logger.Write(LogLevel.Debug,
                    () =>
                            $"Flushing expired channel {expired.Item1} key {expired.Item2} with {fromCache.Item2.Count} objects");

                var translatedEvents = fromCache.Item2.Select(x => new WritableEvent
                {
                    Descriptor = new EventDescriptor
                    {
                        EntityType = "DELAY",
                        StreamType = StreamTypes.Delayed,
                        Bucket = Assembly.GetEntryAssembly().FullName,
                        StreamId = expired.Item1,
                        Timestamp = DateTime.UtcNow,
                        Headers = new Dictionary<string, string>()
                    },
                    Event = x,
                });
                try
                {
                    var streamName = flushState.StreamGen(typeof(EventStoreDelayed), StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, expired.Item1);
                    await flushState.Store.WriteEvents(streamName, translatedEvents, null).ConfigureAwait(false);
                    MemCacheSize.Decrement(fromCache.Item2.Count);
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
            });

        }

        public EventStoreDelayed(IStoreEvents store, TimeSpan? flushInterval, StreamIdGenerator streamGen)
        {
            _store = store;
            _streamGen = streamGen;

            if (_flusher == null && flushInterval.HasValue)
            {
                // Add a process exit event handler to flush cached delayed events before exiting the app
                // Not perfect in the case of a fatal app error - but something
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => Flush(store, all: true).Wait();
                _flusher = Timer.Repeat((s) => Flush(s), store, flushInterval.Value);
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
                }
            }

            if (ex == null)
            {
                Logger.Write(LogLevel.Debug, () => $"Putting {_uncommitted.Count()} delayed streams into mem cache");

                _inFlightMemCache.Clear();
                // Anything with a specific key goes into memcache
                foreach (var kv in _uncommitted.Where(x => !string.IsNullOrEmpty( x.Key.Item2)))
                {
                    MemCache.AddOrUpdate(kv.Key,
                        (key) =>
                            new Tuple<DateTime, List<object>>(DateTime.UtcNow, kv.Value),
                        (key, existing) =>
                            new Tuple<DateTime, List<object>>(DateTime.UtcNow, existing.Item2.Concat(kv.Value).ToList())
                        );
                    MemCacheSize.Increment(kv.Value.Count);
                }

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
                            var streamName = _streamGen(typeof(EventStoreDelayed), StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, kv.Key.Item1);
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
                                new Tuple<DateTime, List<object>>(DateTime.UtcNow, kv.Value),
                        (key, existing) =>
                            new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                                existing.Item2.Concat(kv.Value).ToList())
                    );
                    MemCacheSize.Increment(kv.Value.Count);


                }).ConfigureAwait(false);
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
                return DateTime.UtcNow - (DateTime)cached.Item2;
            }

            // Try metadata
            try
            {
                var metadata = await _store.GetMetadata(streamName, "At").ConfigureAwait(false);
                if (!string.IsNullOrEmpty(metadata))
                {
                    var at = int.Parse(metadata);
                    AgeSizeCache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, at);

                    Logger.Write(LogLevel.Debug, () => $"Got age from metadata of delayed channel [{channel}]");
                    var age = TimeSpan.FromSeconds(DateTime.UtcNow.ToUnixTime() - at);
                    if (age > TimeSpan.FromMinutes(30))
                        SlowLogger.Write(LogLevel.Warn, () => $"Delayed channel [{channel}] is {age.TotalMinutes} minutes old!");

                    DelayedAge.Update(age.Ticks);
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
                    AgeSizeCache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, firstEvent.Single().Descriptor.Timestamp);

                    Logger.Write(LogLevel.Debug, () => $"Got age from first event of delayed channel [{channel}]");
                    var age = DateTime.UtcNow - firstEvent.Single().Descriptor.Timestamp;
                    if (age > TimeSpan.FromMinutes(30))
                        SlowLogger.Write(LogLevel.Warn, () => $"Delayed channel [{channel}] is {age.TotalMinutes} minutes old!");
                    DelayedAge.Update(age.Ticks);
                    return age;
                }
            }
            catch (NotFoundException) { }

            // failed to get age from store, store a negative result
            AgeSizeCache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
            Logger.Write(LogLevel.Debug, () => $"Failed to get age of delayed channel [{channel}]");
            return null;
        }

        public async Task<TimeSpan?> Age(string channel, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}] key [{key}]");

            var channelAge = await ChannelAge(channel).ConfigureAwait(false);
            if (string.IsNullOrEmpty(key))
                return channelAge;

            // Get age from memcache
            var specificKey = new Tuple<string, string>(channel, key);
            Tuple<DateTime, List<object>> temp;
            if (!MemCache.TryGetValue(specificKey, out temp))
                return channelAge;
            var specificAge = DateTime.UtcNow - temp.Item1;

            return channelAge.HasValue && channelAge.Value > specificAge ? channelAge.Value : specificAge;
        }

        public async Task<int> ChannelSize(string channel)
        {
            var streamName = _streamGen(typeof(EventStoreDelayed), StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}]");

            // Try cache
            Tuple<DateTime, object> cached;
            if (AgeSizeCache.TryGetValue($"{streamName}.size", out cached) && (DateTime.UtcNow - cached.Item1).TotalSeconds < 5)
            {
                Logger.Write(LogLevel.Debug, () => $"Got size from cache for channel [{channel}]");
                if (cached.Item2 == null)
                    return 0;
                return (int)cached.Item2 + 1;
            }

            // Read last processed position + last event position
            int lastPosition = StreamPosition.Start;
            try
            {
                var metadata = await _store.GetMetadata(streamName, "Position").ConfigureAwait(false);
                if (!String.IsNullOrEmpty(metadata))
                    lastPosition = int.Parse(metadata) + 1;
            }
            catch (FrozenException)
            {
                // Someone else is processing
                AgeSizeCache[$"{streamName}.size"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
                Logger.Write(LogLevel.Debug, () => $"Size is unavailable from delayed channel [{channel}] - stream frozen");
                return 0;
            }
            try
            {
                var lastEvent = await _store.GetEventsBackwards(streamName, StreamPosition.End, 1).ConfigureAwait(false);
                if (lastEvent != null && lastEvent.Any())
                {
                    AgeSizeCache[$"{streamName}.size"] = new Tuple<DateTime, object>(DateTime.UtcNow, lastEvent.Single().Descriptor.Version - lastPosition);

                    Logger.Write(LogLevel.Debug, () => $"Got size from metadata and last event of delayed channel [{channel}]");
                    var size = ((lastEvent.Single().Descriptor.Version - lastPosition) + 1);
                    if (size > 5000)
                        SlowLogger.Write(LogLevel.Warn, () => $"Delayed channel [{channel}] size is {size}!");
                    DelayedSize.Update(size);
                    return size;
                }
            }
            catch (NotFoundException) { }

            // cache that we dont have one yet
            AgeSizeCache[$"{streamName}.size"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
            Logger.Write(LogLevel.Debug, () => $"Failed to get size of delayed channel [{channel}]");
            return 0;
        }

        public async Task<int> Size(string channel, string key = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}] key [{key}]");

            var channelSize = await ChannelSize(channel).ConfigureAwait(false);
            if (string.IsNullOrEmpty(key))
                return channelSize;

            // Get age from memcache
            var specificKey = new Tuple<string, string>(channel, key);
            Tuple<DateTime, List<object>> temp;
            if (!MemCache.TryGetValue(specificKey, out temp))
                return channelSize;
            var specificSize = temp.Item2.Count;

            return channelSize + specificSize;
        }

        public Task AddToQueue(string channel, object queued, string key = null)
        {
            var streamName = _streamGen(typeof(EventStoreDelayed), StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);
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

        private async Task<IEnumerable<object>> PullChannel(string channel, int? max = null)
        {
            var streamName = _streamGen(typeof(EventStoreDelayed), StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed objects from channel [{channel}]");

            // Clear cache, even if stream is recently pulled Age and Size should be recalced next message
            Tuple<DateTime, object> temp;
            AgeSizeCache.TryRemove($"{streamName}.size", out temp);
            AgeSizeCache.TryRemove($"{streamName}.age", out temp);

            // If a stream has been attempted to pull recently (<30 seconds) don't try again
            lock (RecentLock)
            {
                if (RecentlyPulled.ContainsKey(streamName))
                {
                    Logger.Write(LogLevel.Debug, () => $"Channel [{channel}] was pulled by this instance recently - leaving it alone");
                    return new object[] { }.AsEnumerable();
                }
                RecentlyPulled.Add(streamName, DateTime.UtcNow.AddSeconds(5));
            }

            // Check if someone else is already processing
            lock (_lock)
            {
                if (_inFlightChannel.ContainsKey(channel))
                {
                    Logger.Write(LogLevel.Debug, () => $"Channel [{channel}] is already being processed by this pipeline");
                    return new object[] { }.AsEnumerable();
                }
            }

            IEnumerable<IWritableEvent> delayed = null;
            var didFreeze = false;
            try
            {
                try
                {
                    // Pull metadata before freezing
                    int lastPosition = StreamPosition.Start;
                    var metadata = await _store.GetMetadata(streamName, "Position").ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(metadata))
                        lastPosition = int.Parse(metadata) + 1;

                    await _store.WriteMetadata(streamName, frozen: true, owner: Defaults.Instance).ConfigureAwait(false);
                    didFreeze = true;

                    delayed = await _store.GetEvents(streamName, lastPosition, count: max).ConfigureAwait(false) ?? new IWritableEvent[] { }.AsEnumerable();

                    if (!delayed.Any())
                        throw new Exception("No delayed messages");

                    // Record events as InFlight
                    var info = new InFlightInfo
                    {
                        At = DateTime.UtcNow,
                        Position = delayed.Last().Descriptor.Version
                    };

                    lock (_lock) _inFlightChannel.Add(channel, info);
                }
                catch (ArgumentException)
                {
                    Logger.Write(LogLevel.Debug, () => $"Delayed channel [{channel}] already being processed");
                    throw;
                }
                catch (VersionException)
                {
                    Logger.Write(LogLevel.Debug, () => $"Delayed channel [{channel}] is currently frozen");
                    throw;
                }
            }
            catch (Exception)
            {
                try
                {
                    if (didFreeze)
                        await _store.WriteMetadata(streamName, frozen: false).ConfigureAwait(false);
                }
                catch (VersionException)
                {
                    Logger.Write(LogLevel.Error, () => $"Received VersionException while unfreezing channel [{channel}] - should never happen");
                }
            }
            if (delayed == null)
                return new object[] { };


            var discovered = delayed.Select(x => x.Event);
            DelayedRead.Update(discovered.Count());

            Logger.Write(LogLevel.Debug, () => $"Got {discovered.Count()} delayed messages from channel [{channel}]");
            return discovered;
        }

        public async Task<IEnumerable<object>> Pull(string channel, string key = null, int? max = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed channel [{channel}] key [{key}]");

            var specificKey = new Tuple<string, string>(channel, key);

            Tuple<DateTime, List<object>> fromCache;

            // Check memcache even if key == null because messages failing to save to ES are put in memcache
            if (!MemCache.TryRemove(specificKey, out fromCache))
                fromCache = null;
            
            var discovered = fromCache?.Item2.GetRange(0, Math.Min( max ?? int.MaxValue, fromCache.Item2.Count)).ToList() ?? new List<object>();
            if (max.HasValue)
                fromCache?.Item2.RemoveRange(0, Math.Min( max.Value, fromCache.Item2.Count));
            else
                fromCache?.Item2.Clear();

            // Add back into memcache if Max was used and some elements remain
            if (fromCache != null && fromCache.Item2.Any())
            {
                lock (_lock) _inFlightMemCache.Add(specificKey, discovered);
                MemCache.AddOrUpdate(specificKey,
                    (_) =>
                            new Tuple<DateTime, List<object>>(DateTime.UtcNow, fromCache.Item2),
                    (_, existing) =>
                        new Tuple<DateTime, List<object>>(DateTime.UtcNow,
                            existing.Item2.Concat(fromCache.Item2).ToList()));
            }
            MemCacheSize.Decrement(discovered.Count);

            // Skip getting from store if max already hit
            if (discovered.Count >= max)
            {
                Logger.Write(LogLevel.Debug, () => $"Pulled max events {discovered.Count} from delayed channel [{channel}] key [{key}], skipping store");
                return discovered;
            }

            var fromStore = await PullChannel(channel, max - discovered.Count).ConfigureAwait(false);
            return discovered.Concat(fromStore);
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
