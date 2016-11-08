using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
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
        public int Retries { get; set; }
        public ContextBag Bag { get; set; }

        public class Snapshot
        {
            public DateTime Created { get; set; }
            public int Position { get; set; }
        }
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventStoreDelayed));

        private readonly IStoreEvents _store;

        private Dictionary<string, Tuple<int?, Snapshot>> _inFlight;
        private List<Tuple<string, WritableEvent>> _uncommitted;

        public EventStoreDelayed(IStoreEvents store)
        {
            _store = store;
        }


        public Task Begin()
        {
            _uncommitted = new List<Tuple<string, WritableEvent>>();
            _inFlight = new Dictionary<string, Tuple<int?, Snapshot>>();
            return Task.CompletedTask;
        }

        public Task End(Exception ex = null)
        {
            var streams = _uncommitted.GroupBy(x => x.Item1).Select(x => _store.WriteEvents(x.Key, x.Select(y => y.Item2), null));

            return Task.WhenAll(streams.Concat(_inFlight.Select(x => ex == null ? Ack(x.Key) : NAck(x.Key))));
        }

        public async Task<TimeSpan?> Age(string channel)
        {
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}]");

            var read = await _store.GetEventsBackwards($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null && read.Any())
            {
                var snapshot = read.Single().Event as Snapshot;
                return DateTime.UtcNow - snapshot.Created;
            }
            return null;
        }

        public async Task<int> Size(string channel)
        {
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}]");
            
            var start = StreamPosition.Start;
            var read = await _store.GetEventsBackwards($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null && read.Any())
            {
                var snapshot = read.Single().Event as Snapshot;
                start = snapshot.Position + 1;
            }
            read = await _store.GetEventsBackwards(streamName, StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null)
                return read.Single().Descriptor.Version - start;
            
            return 0;
        }

        public async Task<int> AddToQueue(string channel, object queued)
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

            var start = StreamPosition.Start;
            var read = await _store.GetEventsBackwards($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null && read.Any())
            {
                var snapshot = read.Single().Event as Snapshot;
                start = snapshot.Position + 1;
            }

            read = await _store.GetEventsBackwards(streamName, StreamPosition.End, 1).ConfigureAwait(false);

            _uncommitted.Add(new Tuple<string, WritableEvent>(streamName, @event));

            if (read == null || !read.Any())
                return 0;
            
            return read.Single().Descriptor.Version - start;
        }

        public async Task<IEnumerable<object>> Pull(string channel)
        {
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed objects from channel [{channel}]");

            // Check if someone else is already processing
            if (_inFlight.ContainsKey(channel))
                return new object[] { }.AsEnumerable();

            try
            {
                await _store.WriteMetadata(streamName, frozen: true, owner: Defaults.Instance).ConfigureAwait(false);
            }
            catch(VersionException)
            {
                Logger.Write(LogLevel.Debug, () => $"Delayed channel [{channel}] is currently frozen");
                return new object[] { }.AsEnumerable();
            }

            var start = StreamPosition.Start;
            var read = await _store.GetEventsBackwards($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null && read.Any())
            {
                var snapshot = read.Single().Event as Snapshot;
                start = snapshot.Position + 1;
            }
            var delayed = await _store.GetEvents(streamName, start).ConfigureAwait(false);

            Logger.Write(LogLevel.Debug, () => $"Got {delayed?.Count() ?? 0} delayed from channel [{channel}]");

            if (delayed == null || !delayed.Any())
                return new object[] { }.AsEnumerable();


            // Record events as InFlight
            var snap = new Snapshot { Created = DateTime.UtcNow, Position = delayed.Last().Descriptor.Version };
            _inFlight.Add(channel, new Tuple<int?, Snapshot>((read?.Any() ?? false) ? read?.Single().Descriptor.Version : (int?)null, snap));
            
            return delayed.Select(x => x.Event);
        }

        private async Task Ack(string channel)
        {
            if (!_inFlight.ContainsKey(channel))
                return;

            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            var snap = _inFlight[channel];
            _inFlight.Remove(channel);

            var @event = new WritableEvent
            {
                Descriptor = new EventDescriptor { EntityType = "DELAY", Timestamp = DateTime.UtcNow },
                Event = snap.Item2
            };
            try
            {
                if (await _store.WriteEvents($"{streamName}.SNAP", new[] { @event }, null,
                        expectedVersion: snap.Item1).ConfigureAwait(false) == 1)
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
            if (!_inFlight.ContainsKey(channel))
                return;

            // Remove the freeze so someone else can run the delayed
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            _inFlight.Remove(channel);
            // We've read all delayed events, tell eventstore it can scavage all of them
            await _store.WriteMetadata(streamName, frozen: false).ConfigureAwait(false);
        }

    }
}
