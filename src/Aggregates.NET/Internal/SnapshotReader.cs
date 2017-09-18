using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;


namespace Aggregates.Internal
{
    /// <summary>
    /// Reads snapshots from a snapshot projection, storing what we get in memory for use in event handlers
    /// (Faster than ReadEventsBackwards everytime we want to get a snapshot from ES [especially for larger snapshots])
    /// We could just cache snapshots for a certian period of time but then we'll have to deal with eviction options
    /// </summary>
    class SnapshotReader : ISnapshotReader, IEventSubscriber
    {
        private static readonly ILog Logger = LogProvider.GetLogger("SnapshotReader");
        
        // Todo: upgrade to LRU cache?
        private static readonly ConcurrentDictionary<string, Tuple<DateTime, ISnapshot>> Snapshots = new ConcurrentDictionary<string, Tuple<DateTime, ISnapshot>>();
        private static readonly ConcurrentDictionary<string, long> TruncateBefore = new ConcurrentDictionary<string, long>();

        private static Task _truncate;
        private static Task _snapshotExpiration;
        private static int _truncating;

        private CancellationTokenSource _cancelation;
        private string _endpoint;
        private Version _version;

        private readonly IMetrics _metrics;
        private readonly IEventStoreConsumer _consumer;

        public SnapshotReader(IMetrics metrics, IStoreEvents store, IEventStoreConsumer consumer)
        {
            _metrics = metrics;
            _consumer = consumer;

            if (Interlocked.CompareExchange(ref _truncating, 1, 0) == 1) return;

            // Writes truncateBefore metadata to snapshot streams to let ES know it can delete old snapshots
            // its done here so that we actually get the snapshot before its deleted
            _truncate = Timer.Repeat(async (state) =>
            {
                var eventstore = state as IStoreEvents;

                var truncates = TruncateBefore.Keys.ToList();

                await truncates.WhenAllAsync(async x =>
                {
                    long tb;
                    if (!TruncateBefore.TryRemove(x, out tb))
                        return;

                    try
                    {
                        await eventstore.WriteMetadata(x, truncateBefore: tb).ConfigureAwait(false);
                    }
                    catch { }
                });
            }, store, TimeSpan.FromMinutes(5), "snapshot truncate before");

            _snapshotExpiration = Timer.Repeat((state) =>
            {
                var m = state as IMetrics;

                var expired = Snapshots.Where(x => (DateTime.UtcNow - x.Value.Item1) > TimeSpan.FromMinutes(5)).Select(x => x.Key)
                    .ToList();

                Tuple<DateTime, ISnapshot> temp;
                foreach (var key in expired)
                    if (Snapshots.TryRemove(key, out temp))
                        m.Decrement("Snapshots Stored", Unit.Items);

                return Task.CompletedTask;
            }, metrics, TimeSpan.FromMinutes(5), "expires snapshots from the cache");
        }

        public async Task Setup(string endpoint, Version version)
        {
            _cancelation = new CancellationTokenSource();
            _endpoint = endpoint;
            _version = version;
            await _consumer.EnableProjection("$by_category").ConfigureAwait(false);
        }

        public Task Connect()
        {
            // by_category projection of all events in category "SNAPSHOT"
            var stream = $"$ce-{StreamTypes.Snapshot}";
            return Reconnect(stream);

        }
        public Task Shutdown()
        {
            _cancelation.Cancel();
            return Task.CompletedTask;
        }

        private Task Reconnect(string stream)
        {
            return _consumer.SubscribeToStreamEnd(stream, _cancelation.Token, onEvent, () => Reconnect(stream));
        }

        private void onEvent(string stream, long position, IFullEvent e)
        {
            var snapshot = new Snapshot
            {
                EntityType = e.Descriptor.EntityType,
                Bucket = e.Descriptor.Bucket,
                StreamId = e.Descriptor.StreamId,
                Timestamp = e.Descriptor.Timestamp,
                Version = e.Descriptor.Version,
                Payload = e.Event as IState
            };

            Logger.Write(LogLevel.Debug, () => $"Got snapshot of stream id [{snapshot.StreamId}] bucket [{snapshot.Bucket}] entity [{snapshot.EntityType}] version {snapshot.Version}");
            Snapshots.AddOrUpdate(stream, (key) =>
            {
                _metrics.Increment("Snapshots Stored", Unit.Items);
                return new Tuple<DateTime, ISnapshot>(DateTime.UtcNow, snapshot);
            }, (key, existing) =>
            {
                TruncateBefore[key] = position;
                return new Tuple<DateTime, ISnapshot>(DateTime.UtcNow, snapshot);
            });
        }

        public Task<ISnapshot> Retreive(string stream)
        {
            Tuple<DateTime, ISnapshot> snapshot;
            if (!Snapshots.TryGetValue(stream, out snapshot))
                return Task.FromResult((ISnapshot)null);

            // Update timestamp so snapshot doesn't expire
            Snapshots.TryUpdate(stream, new Tuple<DateTime, ISnapshot>(DateTime.UtcNow, snapshot.Item2), snapshot);

            return Task.FromResult(snapshot.Item2);
        }

        public void Dispose()
        {
            Snapshots.Clear();
            TruncateBefore.Clear();
        }
    }

}
