using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Metrics.Utils;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;

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
        private readonly IMessageMapper _mapper;

        public DelayedChannel(IEventStoreConnection client, IMessageMapper mapper)
        {
            _client = client;
            _mapper = mapper;
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
            var streamName = $"DELAY.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Appending delayed object to channel [{channel}]");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper)
            };

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
            var streamName = $"DELAY.{channel}";
            Logger.Write(LogLevel.Debug, () => $"Pulling delayed objects from channel [{channel}]");
            
            var start = StreamPosition.Start;
            var read = await _client.ReadStreamEventsBackwardAsync($"{streamName}.SNAP", StreamPosition.End, 1, false).ConfigureAwait(false);
            if (read.Status == SliceReadStatus.Success && read.Events.Any())
            {
                var snapshot = read.Events[0].Event.Data.Deserialize<Snapshot>(new JsonSerializerSettings());
                start = snapshot.Position + 1;
            }
            var endOfChannel = await _client.ReadStreamEventsBackwardAsync(streamName, StreamPosition.End, 1, false).ConfigureAwait(false);

            if (endOfChannel.Status != SliceReadStatus.Success)
                return new object[] { }.AsEnumerable();

            var snap = new Snapshot { Created = DateTime.UtcNow, Position = endOfChannel.LastEventNumber };
            var @event = new EventData(
                Guid.NewGuid(),
                typeof(Snapshot).AssemblyQualifiedName,
                true,
                snap.Serialize(new JsonSerializerSettings()).AsByteArray(),
                new byte[] { }
            );
            try
            {
                // Attempt to save the new snapshot at the same point, if failed assume someone else beat us to it (they did)
                Logger.Write(LogLevel.Debug, () => $"Failed to save updated snapshot for channel [{channel}]");
                await
                    _client.AppendToStreamAsync($"{streamName}.SNAP", start == StreamPosition.Start ? ExpectedVersion.NoStream : read.LastEventNumber, @event)
                        .ConfigureAwait(false);
                if (start == StreamPosition.Start)
                {
                    // Only need to keep a few snapshots around
                    var metadata = StreamMetadata.Build().SetMaxCount(5);
                    await _client.SetStreamMetadataAsync($"{streamName}.SNAP", ExpectedVersion.Any, metadata).ConfigureAwait(false);
                }

            }
            catch (WrongExpectedVersionException)
            {
                return new object[] {}.AsEnumerable();
            }
            var events = new List<ResolvedEvent>();
            StreamEventsSlice current;
            var currentPos = start;
            Logger.Write(LogLevel.Debug, () => $"Getting {endOfChannel.LastEventNumber - start} delayed from channel [{channel}] starting at {start}");
            do
            {
                var take = 100;
                current = await _client.ReadStreamEventsForwardAsync(streamName, currentPos, take, false).ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Retreived {current.Events.Length} delayed from position {currentPos}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                events.AddRange(current.Events);

                currentPos = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.Write(LogLevel.Debug, () => $"Finished getting all delayed from channel [{channel}]");

            if (start != StreamPosition.Start)
            {
                // Tell eventstore it can scavage all the events now
                var metadata = StreamMetadata.Build().SetTruncateBefore(start);
                await _client.SetStreamMetadataAsync(streamName, ExpectedVersion.Any, metadata).ConfigureAwait(false);
            }

            // Todo: there is no real way to know if the data retrieved this way was processed.  If one of the delayed events causes an error the entire batch will be lost...
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            return events.Select(x => x.Event.Data.Deserialize<object>(settings));
        }
        
    }
}
