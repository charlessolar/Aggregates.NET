using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Newtonsoft.Json;
using NServiceBus.Logging;

namespace Aggregates.Internal
{
    internal class DelayedChannel : IDelayedChannel
    {
        public class Snapshot
        {
            public DateTime Created { get; set; }
            public int Position { get; set; }
        }
        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreSnapshots));

        private readonly IEventStoreConnection _client;

        public DelayedChannel(IEventStoreConnection client)
        {
            _client = client;
        }

        public async Task<TimeSpan?> Age(string channel)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
            var streamName = $"DELAY.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Getting age of delayed channel [{channel}]");
            
            var read = await _client.ReadEventAsync($"{streamName}.SNAP", StreamPosition.End, false).ConfigureAwait(false);
            if (read.Status == EventReadStatus.Success && read.Event.HasValue)
            {
                var snapshot = read.Event.Value.Event.Data.Deserialize<Snapshot>(settings);
                return DateTime.UtcNow - snapshot.Created;
            }
            return null;
        }

        public async Task<int> Size(string channel)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
            var streamName = $"DELAY.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Getting size of delayed channel [{channel}]");

            var start = StreamPosition.Start;
            var read = await _client.ReadEventAsync($"{streamName}.SNAP", StreamPosition.End, false).ConfigureAwait(false);
            if (read.Status == EventReadStatus.Success && read.Event.HasValue)
            {
                var snapshot = read.Event.Value.Event.Data.Deserialize<Snapshot>(settings);
                start = snapshot.Position + 1;
            }
            read = await _client.ReadEventAsync(streamName, StreamPosition.End, false).ConfigureAwait(false);

            if (read.Status == EventReadStatus.Success)
                return read.EventNumber - start;
            return 0;
        }

        public async Task<int> AddToQueue(string channel, object queued)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
            var streamName = $"DELAY.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Appending delayed object to channel [{channel}]");


            var start = StreamPosition.Start;
            var read = await _client.ReadEventAsync($"{streamName}.SNAP", StreamPosition.End, false).ConfigureAwait(false);
            if (read.Status == EventReadStatus.Success && read.Event.HasValue)
            {
                var snapshot = read.Event.Value.Event.Data.Deserialize<Snapshot>(settings);
                start = snapshot.Position + 1;
            }

            var @event = new EventData(
                Guid.NewGuid(),
                queued.GetType().AssemblyQualifiedName,
                true,
                queued.Serialize(settings).AsByteArray(),
                new byte[] { }
            );

            var result = await _client.AppendToStreamAsync(streamName, ExpectedVersion.Any, @event).ConfigureAwait(false);

            return result.NextExpectedVersion - start;
        }

        public async Task<IEnumerable<object>> Pull(string channel)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
            var streamName = $"DELAY.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed objects from channel [{channel}]");

            var start = StreamPosition.Start;
            var read = await _client.ReadEventAsync($"{streamName}.SNAP", StreamPosition.End, false).ConfigureAwait(false);
            if (read.Status == EventReadStatus.Success && read.Event.HasValue)
            {
                var snapshot = read.Event.Value.Event.Data.Deserialize<Snapshot>(new JsonSerializerSettings());
                start = snapshot.Position + 1;
            }
            read = await _client.ReadEventAsync(streamName, StreamPosition.End, false).ConfigureAwait(false);

            if (read.Status != EventReadStatus.Success)
                return new object[] { }.AsEnumerable();

            var snap = new Snapshot { Created = DateTime.UtcNow, Position = read.EventNumber };
            var @event = new EventData(
                Guid.NewGuid(),
                typeof(Snapshot).AssemblyQualifiedName,
                true,
                snap.Serialize(new JsonSerializerSettings()).AsByteArray(),
                new byte[] { }
            );
            await _client.AppendToStreamAsync($"{streamName}.SNAP", ExpectedVersion.Any, @event).ConfigureAwait(false);

            var events = new List<ResolvedEvent>();
            StreamEventsSlice current;
            Logger.Write(LogLevel.Debug, () => $"Getting {read.EventNumber - start} delayed from channel [{channel}] starting at {start}");
            do
            {
                var take = 100;
                current = await _client.ReadStreamEventsBackwardAsync(streamName, start, take, false).ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Retreived {current.Events.Length} delayed from position {start}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                events.AddRange(current.Events);

                start = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.Write(LogLevel.Debug, () => $"Finished getting all delayed from channel [{channel}]");

            return events.Select(x => x.Event.Data.Deserialize<object>(settings));
        }
    }
}
