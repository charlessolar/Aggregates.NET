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

            var read = await _store.GetEvents($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null)
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
            var read = await _store.GetEvents($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null)
            {
                var snapshot = read.Single().Event as Snapshot;
                start = snapshot.Position + 1;
            }
            read = await _store.GetEvents(streamName, StreamPosition.End, 1).ConfigureAwait(false);
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
            var read = await _store.GetEvents($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null)
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

            var start = StreamPosition.Start;
            var read = await _store.GetEvents($"{streamName}.SNAP", StreamPosition.End, 1).ConfigureAwait(false);
            if (read != null)
            {
                var snapshot = read.Single().Event as Snapshot;
                start = snapshot.Position + 1;
            }
            var delayed = await _store.GetEvents(streamName, start).ConfigureAwait(false);

            Logger.Write(LogLevel.Debug, () => $"Got {delayed?.Count() ?? 0} delayed from channel [{channel}]");

            if (delayed == null || !delayed.Any())
                return new object[] { }.AsEnumerable();

            // First write a new snapshot
            var snap = new Snapshot { Created = DateTime.UtcNow, Position = delayed.Last().Descriptor.Version };
            var @event = new WritableEvent
            {
                Descriptor = new EventDescriptor {EntityType = "DELAY", Timestamp = DateTime.UtcNow},
                Event = snap
            };
            try
            {
                if (await _store.WriteEvents($"{streamName}.SNAP", new[] {@event}, null,
                            expectedVersion: read?.Single().Descriptor.Version) == 1)
                    await _store.WriteMetadata($"{streamName}.SNAP", maxCount: 5).ConfigureAwait(false);
            }
            catch (VersionException)
            {
                Logger.Write(LogLevel.Debug, () => $"Failed to save updated snapshot for channel [{channel}]");
                return new object[] {}.AsEnumerable();
            }
            
                // We've read all delayed events, tell eventstore it can scavage all of them
                await _store.WriteMetadata(streamName, truncateBefore: snap.Position).ConfigureAwait(false);
            

            // Todo: there is no real way to know if the data retrieved this way was processed.  If one of the delayed events causes an error the entire batch will be lost...
            return delayed.Select(x => x.Event);
        }
        
    }
}
