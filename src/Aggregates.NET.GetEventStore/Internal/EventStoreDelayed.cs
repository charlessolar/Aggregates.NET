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
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;

namespace Aggregates.Internal
{
    class EventStoreDelayed : IDelayedChannel
    {
        public class Snapshot
        {
            public DateTime Created { get; set; }
            public int Position { get; set; }
        }
        private static readonly ILog Logger = LogManager.GetLogger(typeof(EventStoreDelayed));
        private static readonly Dictionary<string, Tuple<int?, Snapshot>> InFlight = new Dictionary<string, Tuple<int?, Snapshot>>();

        private readonly IStoreEvents _store;
        private readonly IMessageMapper _mapper;

        public EventStoreDelayed(IStoreEvents store, IMessageMapper mapper)
        {
            _store = store;
            _mapper = mapper;
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

            var nextVersion = await _store.WriteEvents(streamName, new[] {@event}, null).ConfigureAwait(false);

            return nextVersion - start;
        }

        public async Task<IEnumerable<object>> Pull(string channel)
        {
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed objects from channel [{channel}]");

            // Check if someone else is already processing
            if (InFlight.ContainsKey(channel))
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
            InFlight.Add(channel, new Tuple<int?, Snapshot>((read?.Any() ?? false) ? read?.Single().Descriptor.Version : (int?)null, snap));
            
            return delayed.Select(x => x.Event);
        }

        public async Task Ack(string channel)
        {
            if (!InFlight.ContainsKey(channel))
                return;

            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            var snap = InFlight[channel];
            InFlight.Remove(channel);

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

        public async Task NAck(string channel)
        {
            // Remove the freeze so someone else can run the delayed
            var streamName = $"DELAY.{Assembly.GetEntryAssembly().FullName}.{channel}";
            InFlight.Remove(channel);
            // We've read all delayed events, tell eventstore it can scavage all of them
            await _store.WriteMetadata(streamName, frozen: false).ConfigureAwait(false);
        }
    }
}
