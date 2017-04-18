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
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;
using Aggregates.Extensions;


namespace Aggregates.Internal
{
    /// <summary>
    /// Reads snapshots from a snapshot projection, storing what we get in memory for use in event handlers
    /// (Faster than ReadEventsBackwards everytime we want to get a snapshot from ES [especially for larger snapshots])
    /// We could just cache snapshots for a certian period of time but then we'll have to deal with eviction options
    /// </summary>
    class SnapshotReader : ISnapshotReader
    {
        private static readonly ILog Logger = LogManager.GetLogger("SnapshotReader");
        private static readonly Counter SnapshotsSeen = Metric.Counter("Snapshots Seen", Unit.Items, tags: "debug");
        private static readonly Counter StoredSnapshots = Metric.Counter("Snapshots Stored", Unit.Items, tags: "debug");

        private static readonly ConcurrentDictionary<string, ISnapshot> Snapshots = new ConcurrentDictionary<string, ISnapshot>();
        private static readonly ConcurrentDictionary<string, long> TruncateBefore = new ConcurrentDictionary<string, long>();

        private static Task _truncate;
        private static int _truncating;

        private CancellationTokenSource _cancelation;
        private string _endpoint;

        private readonly IEventStoreConsumer _consumer;
        private readonly Compression _compress;

        public SnapshotReader(IStoreEvents store, IMessageMapper mapper, IEventStoreConsumer consumer, Compression compress)
        {
            _consumer = consumer;
            _compress = compress;

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
        }


        public async Task Connect(string endpoint, CancellationToken cancelToken)
        {
            _endpoint = endpoint;
            await _consumer.EnableProjection("$by_category").ConfigureAwait(false);
            _cancelation = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
            await Connect().ConfigureAwait(false);
        }


        private Task Connect()
        {
            // by_category projection of all events in category "SNAPSHOT"
            var stream = $"$ce-SNAPSHOT";
            return _consumer.SubscribeToStreamEnd(stream, _cancelation.Token, onEvent, Connect);

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
                Payload = e.Event
            };
            SnapshotsSeen.Increment();

            Logger.Write(LogLevel.Debug, () => $"Got snapshot of stream id [{snapshot.StreamId}] bucket [{snapshot.Bucket}] entity [{snapshot.EntityType}] version {snapshot.Version}");
            Snapshots.AddOrUpdate(stream, (key) =>
            {
                StoredSnapshots.Increment();
                return snapshot;
            }, (key, existing) =>
            {
                TruncateBefore[key] = position;
                return snapshot;
            });
        }

        public Task<ISnapshot> Retreive(string stream)
        {
            ISnapshot snapshot;
            if (!Snapshots.TryGetValue(stream, out snapshot))
                snapshot = null;
            return Task.FromResult(snapshot);
        }
    }

}
