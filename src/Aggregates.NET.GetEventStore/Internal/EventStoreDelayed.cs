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
using Metrics.Utils;
using Newtonsoft.Json;
using NServiceBus.Extensibility;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Internal
{
    class EventStoreDelayed : IDelayedChannel, IApplicationUnitOfWork
    {
        public IBuilder Builder { get; set; }
        // The number of times the event has been re-run due to error
        public int Retries { get; set; }
        // Will be persisted across retries
        public ContextBag Bag { get; set; }

        private class Snapshot
        {
            public DateTime Created { get; set; }
            public int Position { get; set; }
        }
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventStoreDelayed));
        private static readonly object WaitingLock = new object();
        private static readonly Dictionary<string, List<IWritableEvent>> WaitingToBeWritten = new Dictionary<string, List<IWritableEvent>>();
        private static Timer _flusher;

        private static readonly ConcurrentDictionary<string, object> Cache = new ConcurrentDictionary<string, object>();
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

        private object _lock = new object();
        private Dictionary<string, Tuple<int?, Snapshot>> _inFlight;
        private List<Tuple<string, WritableEvent>> _uncommitted;
        
        static void Flush(object state)
        {
            var eventstore = state as IStoreEvents;

            ReadOnlyDictionary<string, List<IWritableEvent>> waiting;
            lock (WaitingLock)
            {
                waiting = new ReadOnlyDictionary<string, List<IWritableEvent>>(WaitingToBeWritten.ToDictionary(x => x.Key, x => x.Value.ToList()));
                WaitingToBeWritten.Clear();
            }
            Logger.Write(LogLevel.Debug, () => $"Flushing {waiting.Count} channels with {waiting.Values.Sum(x => x.Count())} events");
            foreach (var channel in waiting)
            {
                eventstore.WriteEvents(channel.Key, channel.Value, null).Wait();
            }
        }

        public EventStoreDelayed(IStoreEvents store, int? flushInterval)
        {
            _store = store;

            if (_flusher == null && flushInterval.HasValue)
            {
                // Add a process exit event handler to flush cached delayed events before exiting the app
                // Not perfect in the case of a fatal app error - but something
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => Flush(store);
                _flusher = new Timer(Flush, store, TimeSpan.FromSeconds(flushInterval.Value), TimeSpan.FromSeconds(flushInterval.Value));
            }
        }

        public Task Begin()
        {
            _uncommitted = new List<Tuple<string, WritableEvent>>();
            _inFlight = new Dictionary<string, Tuple<int?, Snapshot>>();
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
                await Task.WhenAll(
                    _uncommitted.GroupBy(x => x.Item1)
                        .Select(x => _store.WriteEvents(x.Key, x.Select(y => y.Item2), null))).ConfigureAwait(false);

            }
        }

        public async Task<TimeSpan?> Age(string channel)
        {
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}]");

            object cached;
            if (Cache.TryGetValue($"{streamName}.age", out cached))
            {
                Logger.Write(LogLevel.Debug, () => $"Got age from cache for channel [{channel}]");
                return DateTime.UtcNow - (DateTime)cached;
            }

            var read = await _store.GetEventsBackwards($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null && read.Any())
            {
                var snapshot = read.Single().Event as Snapshot;
                Cache.TryAdd($"{streamName}.age", snapshot.Created);
                return DateTime.UtcNow - snapshot.Created;
            }

            read = await _store.GetEventsBackwards($"{streamName}", StreamPosition.Start, 1).ConfigureAwait(false);
            if (read != null && read.Any())
            {
                Cache.TryAdd($"{streamName}.age", read.Single().Descriptor.Timestamp);
                return DateTime.UtcNow - read.Single().Descriptor.Timestamp;
            }

            Tuple<string, WritableEvent> existing;
            lock (_lock) existing = _uncommitted.FirstOrDefault(c => c.Item1 == streamName);
            if (existing != null)
            {
                // Dont add to cache because this is a non-committed event, save caching for what we read from ES
                return DateTime.UtcNow - existing.Item2.Descriptor.Timestamp;
            }

            return null;
        }

        public async Task<int> Size(string channel)
        {
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}]");

            int existing;
            lock (_lock) existing = _uncommitted.Count(c => c.Item1 == streamName);

            object cached;
            if (Cache.TryGetValue($"{streamName}.size", out cached))
            {
                Logger.Write(LogLevel.Debug, () => $"Got size from cache for channel [{channel}]");
                return existing + (int)cached + 1;
            }

            var start = StreamPosition.Start;
            var read = await _store.GetEventsBackwards($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null && read.Any())
            {
                var snapshot = read.Single().Event as Snapshot;
                start = snapshot.Position + 1;
            }
            read = await _store.GetEventsBackwards(streamName, StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null)
            {
                Cache.TryAdd($"{streamName}.size", read.Single().Descriptor.Version - start);
                return existing + (read.Single().Descriptor.Version - start) + 1;
            }

            Cache.TryAdd($"{streamName}.size", existing);
            return existing;
        }

        public Task AddToQueue(string channel, object queued)
        {
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Appending delayed object to channel [{channel}]");

            var @event = new WritableEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = "DELAY",
                    Timestamp = DateTime.UtcNow,
                },
                Event = queued,
            };

            lock (_lock) _uncommitted.Add(new Tuple<string, WritableEvent>(streamName, @event));

            return Task.CompletedTask;
        }

        public async Task<IEnumerable<object>> Pull(string channel)
        {
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed objects from channel [{channel}]");

            object temp;
            Cache.TryRemove($"{streamName}.size", out temp);
            Cache.TryRemove($"{streamName}.age", out temp);

            // If a stream has been attempted to pull recently (<30 seconds) don't try again
            lock (RecentLock)
            {
                if (RecentlyPulled.ContainsKey(streamName))
                {
                    Logger.Write(LogLevel.Debug, () => $"Channel [{channel}] was pulled by this instance recently - leaving it alone");
                    return new object[] { }.AsEnumerable();
                }
                RecentlyPulled.Add(streamName, DateTime.UtcNow.AddSeconds(30));
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
                    await _store.WriteMetadata(streamName, frozen: true, owner: Defaults.Instance).ConfigureAwait(false);
                    didFreeze = true;

                    var start = StreamPosition.Start;
                    var read =
                        await
                            _store.GetEventsBackwards($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
                    if (read != null && read.Any())
                    {
                        var snapshot = read.Single().Event as Snapshot;
                        start = snapshot.Position + 1;
                    }
                    delayed = await _store.GetEvents(streamName, start).ConfigureAwait(false) ?? new IWritableEvent[] { }.AsEnumerable();

                    Logger.Write(LogLevel.Debug, () => $"Got {delayed?.Count() ?? 0} delayed from channel [{channel}]");

                    // Record events as InFlight
                    var snap = new Snapshot { Created = DateTime.UtcNow, Position = delayed?.Last().Descriptor.Version ?? 0 };

                    lock (_lock)
                    {
                        _inFlight.Add(channel,
                            new Tuple<int?, Snapshot>(
                                (read?.Any() ?? false) ? read?.Single().Descriptor.Version : (int?)null,
                                snap));
                    }

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
                    return new object[] { }.AsEnumerable();
                }
                catch (VersionException)
                {
                }
            }
            var discovered = delayed.Select(x => x.Event);
            List<Tuple<string, WritableEvent>> existing;
            lock (_lock)
            {
                existing = _uncommitted.Where(c => c.Item1 == streamName).ToList();
                foreach (var e in existing)
                    _uncommitted.Remove(e);
            }
            return discovered.Concat(existing.Select(x => x.Item2.Event));
        }

        private async Task Ack(string channel)
        {
            lock (_lock)
            {
                if (!_inFlight.ContainsKey(channel))
                    return;
            }
            Logger.Write(LogLevel.Debug, () => $"Acking channel {channel}");

            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Tuple<int?, Snapshot> snap;
            lock (_lock)
            {
                snap = _inFlight[channel];
                _inFlight.Remove(channel);
            }
            var @event = new WritableEvent
            {
                Descriptor = new EventDescriptor { EntityType = "DELAY", Timestamp = DateTime.UtcNow },
                Event = snap.Item2
            };
            try
            {
                if (await _store.WriteEvents($"{streamName}.SNAP", new[] { @event }, null, expectedVersion: snap.Item1 ?? ExpectedVersion.NoStream).ConfigureAwait(false) == 1)
                    // New stream, write metadata
                    await _store.WriteMetadata($"{streamName}.SNAP", maxCount: 5).ConfigureAwait(false);
            }
            catch (VersionException)
            {
                Logger.Write(LogLevel.Error, () => $"Failed to save updated snapshot for channel [{channel}]");
                throw;
            }
            // We've read all delayed events, tell eventstore it can scavage all of them
            await _store.WriteMetadata(streamName, truncateBefore: snap.Item2.Position, frozen: false).ConfigureAwait(false);
        }

        private async Task NAck(string channel)
        {
            lock (_lock)
            {
                if (!_inFlight.ContainsKey(channel))
                    return;
            }
            Logger.Write(LogLevel.Debug, () => $"NAcking channel {channel}");

            // Remove the freeze so someone else can run the delayed
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            lock (_lock) _inFlight.Remove(channel);
            // Failed to process messages, unfreeze stream
            await _store.WriteMetadata(streamName, frozen: false).ConfigureAwait(false);
        }

    }
}
