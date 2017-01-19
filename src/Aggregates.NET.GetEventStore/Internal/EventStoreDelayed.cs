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
        private static readonly Histogram Delayed = Metric.Histogram("Delayed Channel Size", Unit.Items);
        private static readonly ILog Logger = LogManager.GetLogger("EventStoreDelayed");
        private static readonly object WaitingLock = new object();
        private static readonly Dictionary<string, List<IWritableEvent>> WaitingToBeWritten = new Dictionary<string, List<IWritableEvent>>();
        private static Timer _flusher;

        private static readonly ConcurrentDictionary<string, Tuple<DateTime, object>> Cache = new ConcurrentDictionary<string, Tuple<DateTime, object>>();
        private static readonly Dictionary<string, DateTime> RecentlyPulled = new Dictionary<string, DateTime>();
        private static readonly object RecentLock = new object();

        private static readonly Timer Expiring = new Timer(_ =>
        {
            lock (RecentLock)
            {
                var expired = RecentlyPulled.Where(x => x.Value < DateTime.UtcNow).ToList();
                foreach (var e in expired)
                    RecentlyPulled.Remove(e.Key);
            }
        }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));


        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _streamGen;
        private readonly Type _delayType;

        private readonly object _lock = new object();
        private Dictionary<string, InFlightInfo> _inFlight;
        private List<Tuple<string, WritableEvent>> _uncommitted;

        static void Flush(object state)
        {
            var eventstore = state as IStoreEvents;

            ReadOnlyDictionary<string, List<IWritableEvent>> waiting;
            lock (WaitingLock)
            {
                waiting = new ReadOnlyDictionary<string, List<IWritableEvent>>(WaitingToBeWritten.Where(x => x.Value.Any()).ToDictionary(x => x.Key, x => x.Value.ToList()));
                WaitingToBeWritten.Clear();
            }
            Logger.Write(LogLevel.Debug, () => $"Flushing {waiting.Count} channels with {waiting.Values.Sum(x => x.Count())} events");

            Task.Run(() => waiting.ToArray().StartEachAsync(3, async (channel) =>
            {
                try
                {
                    await eventstore.WriteEvents(channel.Key, channel.Value, null).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.Write(LogLevel.Warn,
                        () => $"Failed to write to channel [{channel.Key}].  Exception: {e.GetType().Name}: {e.Message}");
                    // Failed to write to ES - requeue events
                    lock (WaitingLock)
                    {
                        if (!WaitingToBeWritten.ContainsKey(channel.Key))
                            WaitingToBeWritten[channel.Key] = new List<IWritableEvent>();
                        WaitingToBeWritten[channel.Key].AddRange(channel.Value);
                    }
                }
            })).Wait();

        }

        public EventStoreDelayed(IStoreEvents store, TimeSpan? flushInterval, StreamIdGenerator streamGen)
        {
            _store = store;
            _streamGen = streamGen;
            _delayType = this.GetType();

            if (_flusher == null && flushInterval.HasValue)
            {
                // Add a process exit event handler to flush cached delayed events before exiting the app
                // Not perfect in the case of a fatal app error - but something
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => Flush(store);
                _flusher = new Timer(Flush, store, flushInterval.Value, flushInterval.Value);
            }
        }

        public Task Begin()
        {
            _uncommitted = new List<Tuple<string, WritableEvent>>();
            _inFlight = new Dictionary<string, InFlightInfo>();
            return Task.CompletedTask;
        }

        public async Task End(Exception ex = null)
        {
            Logger.Write(LogLevel.Debug, () => $"Saving {_inFlight.Count()} {(ex == null ? "ACKs" : "NACKs")}");
            await Task.WhenAll(_inFlight.ToList().Select(x => ex == null ? Ack(x.Key) : NAck(x.Key))).ConfigureAwait(false);
            if (ex == null && _flusher != null)
            {
                Logger.Write(LogLevel.Debug, () => $"Scheduling save of {_uncommitted.Count()} delayed streams");

                lock (WaitingLock)
                {
                    foreach (var stream in _uncommitted.GroupBy(x => x.Item1))
                    {
                        if (!WaitingToBeWritten.ContainsKey(stream.Key))
                            WaitingToBeWritten[stream.Key] = new List<IWritableEvent>();
                        WaitingToBeWritten[stream.Key].AddRange(stream.Select(x => x.Item2));
                    }
                }
            }
            if (ex == null && _flusher == null)
            {
                Logger.Write(LogLevel.Debug, () => $"Saving {_uncommitted.Count()} delayed streams");
                await _uncommitted.GroupBy(x => x.Item1).ToArray().StartEachAsync(3, (x) => _store.WriteEvents(x.Key, x.Select(y => y.Item2), null)).ConfigureAwait(false);

            }
        }

        public async Task<TimeSpan?> Age(string channel)
        {
            var streamName = _streamGen(_delayType, StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}]");

            Tuple<DateTime, object> cached;
            if (Cache.TryGetValue($"{streamName}.age", out cached) && (DateTime.UtcNow - cached.Item1).TotalSeconds < 5)
            {
                Logger.Write(LogLevel.Debug, () => $"Got age from cache for channel [{channel}]");
                if (cached.Item2 == null)
                {

                    Tuple<string, WritableEvent> existing;
                    lock (_lock) existing = _uncommitted.FirstOrDefault(c => c.Item1 == streamName);
                    return DateTime.UtcNow - existing.Item2.Descriptor.Timestamp;
                }
                return DateTime.UtcNow - (DateTime)cached.Item2;
            }

            try
            {
                int at;
                var metadata = await _store.GetMetadata(streamName, "At").ConfigureAwait(false);
                if (!string.IsNullOrEmpty(metadata))
                {
                    at = int.Parse(metadata);
                    Logger.Write(LogLevel.Debug, () => $"Got age from metadata of delayed channel [{channel}]");
                    return TimeSpan.FromSeconds(DateTime.UtcNow.ToUnixTime() - at);
                }
            }
            catch (FrozenException)
            {
                // Someone else is processing
                Cache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
                Logger.Write(LogLevel.Debug, () => $"Age is unavailable from delayed channel [{channel}] - stream frozen");
                return null;
            }

            try
            {
                var firstEvent = await _store.GetEvents(streamName, StreamPosition.Start, 1).ConfigureAwait(false);
                if (firstEvent != null && firstEvent.Any())
                {
                    Cache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow,
                        firstEvent.Single().Descriptor.Timestamp);
                    Logger.Write(LogLevel.Debug, () => $"Got age from first event of delayed channel [{channel}]");
                    return DateTime.UtcNow - firstEvent.Single().Descriptor.Timestamp;
                }
            }
            catch (NotFoundException) { }

            {
                Tuple<string, WritableEvent> existing;
                lock (_lock) existing = _uncommitted.FirstOrDefault(c => c.Item1 == streamName);
                Cache[$"{streamName}.age"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
                if (existing != null)
                {
                    Logger.Write(LogLevel.Debug, () => $"Got age from uncommitted of delayed channel [{channel}]");
                    return DateTime.UtcNow - existing.Item2.Descriptor.Timestamp;
                }
            }
            
            return null;
        }

        public async Task<int> Size(string channel)
        {
            var streamName = _streamGen(_delayType, StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}]");


            Tuple<DateTime, object> cached;
            if (Cache.TryGetValue($"{streamName}.size", out cached) && (DateTime.UtcNow - cached.Item1).TotalSeconds < 5)
            {
                Logger.Write(LogLevel.Debug, () => $"Got size from cache for channel [{channel}]");
                if (cached.Item2 == null)
                {
                    int existing;
                    lock (_lock) existing = _uncommitted.Count(x => x.Item1 == streamName);
                    return existing;
                }
                return (int)cached.Item2 + 1;
            }
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
                Cache[$"{streamName}.size"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
                Logger.Write(LogLevel.Debug, () => $"Size is unavailable from delayed channel [{channel}] - stream frozen");
                return 0;
            }
            try
            {
                var lastEvent = await _store.GetEventsBackwards(streamName, StreamPosition.End, 1).ConfigureAwait(false);
                if (lastEvent != null && lastEvent.Any())
                {
                    Cache[$"{streamName}.size"] = new Tuple<DateTime, object>(DateTime.UtcNow,
                        lastEvent.Single().Descriptor.Version - lastPosition);
                    Logger.Write(LogLevel.Debug,
                        () => $"Got size from metadata and last event of delayed channel [{channel}]");
                    return ((lastEvent.Single().Descriptor.Version - lastPosition) + 1);
                }
            }
            catch (NotFoundException) { }

            {
                int existing;
                lock (_lock) existing = _uncommitted.Count(x => x.Item1 == streamName);

                // cache that we dont have one yet
                Cache[$"{streamName}.size"] = new Tuple<DateTime, object>(DateTime.UtcNow, null);
                Logger.Write(LogLevel.Debug, () => $"Got size from uncommitted of delayed channel [{channel}]");
                return existing;
            }
        }

        public Task AddToQueue(string channel, object queued)
        {
            var streamName = _streamGen(_delayType, StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);
            Logger.Write(LogLevel.Debug, () => $"Appending delayed object to channel [{channel}]");

            var @event = new WritableEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = "DELAY",
                    StreamType = StreamTypes.Delayed,
                    Bucket = Assembly.GetEntryAssembly().FullName,
                    StreamId = channel,
                    Timestamp = DateTime.UtcNow,
                    Headers = new Dictionary<string, string>()
                },
                Event = queued,
            };

            lock (_lock) _uncommitted.Add(new Tuple<string, WritableEvent>(streamName, @event));

            return Task.CompletedTask;
        }

        public async Task<IEnumerable<object>> Pull(string channel)
        {
            var streamName = _streamGen(_delayType, StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed objects from channel [{channel}]");

            Tuple<DateTime, object> temp;
            Cache.TryRemove($"{streamName}.size", out temp);
            Cache.TryRemove($"{streamName}.age", out temp);

            // If a stream has been attempted to pull recently (<30 seconds) don't try again
            lock (RecentLock)
            {
                if (RecentlyPulled.ContainsKey(streamName))
                {
                    Logger.Write(LogLevel.Warn, () => $"Channel [{channel}] was pulled by this instance recently - leaving it alone");
                    return new object[] { }.AsEnumerable();
                }
                RecentlyPulled.Add(streamName, DateTime.UtcNow.AddSeconds(5));
            }

            // Check if someone else is already processing
            lock (_lock)
            {
                if (_inFlight.ContainsKey(channel))
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
                    if (!String.IsNullOrEmpty(metadata))
                        lastPosition = int.Parse(metadata) + 1;

                    await _store.WriteMetadata(streamName, frozen: true, owner: Defaults.Instance).ConfigureAwait(false);
                    didFreeze = true;
                    
                    delayed = await _store.GetEvents(streamName, lastPosition).ConfigureAwait(false) ?? new IWritableEvent[] { }.AsEnumerable();

                    if (!delayed.Any())
                        throw new Exception("No delayed messages");

                    // Record events as InFlight
                    var info = new InFlightInfo
                    {
                        At = DateTime.UtcNow,
                        Position = delayed.Last().Descriptor.Version
                    };

                    lock (_lock) _inFlight.Add(channel, info);
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
                return new object[] {};


            var discovered = delayed.Select(x => x.Event);
            List<Tuple<string, WritableEvent>> existing;
            lock (_lock)
            {
                existing = _uncommitted.Where(c => c.Item1 == streamName).ToList();
                foreach (var e in existing)
                    _uncommitted.Remove(e);
            }
            Delayed.Update(discovered.Count() + existing.Count);

            var messages = discovered.Concat(existing.Select(x => x.Item2.Event));
            Logger.Write(LogLevel.Debug, () => $"Got {messages.Count()} delayed messages from channel [{channel}]");
            return messages;
        }

        private async Task Ack(string channel)
        {
            InFlightInfo info;
            lock (_lock)
            {
                if (!_inFlight.ContainsKey(channel))
                    return;
                info = _inFlight[channel];
                _inFlight.Remove(channel);
            }

            Logger.Write(LogLevel.Debug, () => $"Acking channel [{channel}] position {info.Position} at {info.At.ToUnixTime()}");

            var streamName = _streamGen(_delayType, StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);

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
                if (!_inFlight.ContainsKey(channel))
                    return;
            }
            Logger.Write(LogLevel.Debug, () => $"NAcking channel [{channel}]");

            // Remove the freeze so someone else can run the delayed
            var streamName = _streamGen(_delayType, StreamTypes.Delayed, Assembly.GetEntryAssembly().FullName, channel);
            lock (_lock) _inFlight.Remove(channel);
            // Failed to process messages, unfreeze stream
            await _store.WriteMetadata(streamName, frozen: false).ConfigureAwait(false);
        }

    }
}
