using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus.Logging;
using Timer = Metrics.Timer;

namespace Aggregates.Internal
{
    class CatchupClient : IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("CatchupClient");
        private static readonly Counter Snapshots = Metric.Counter("Snapshots Seen", Unit.Items);

        // Todo: config option for "most snapshots stored" and a LRU cache?
        private readonly SortedDictionary<string, IWritableEvent> _snapshots;
        private readonly Action<string, ISnapshot> _onSnapshot;
        private readonly IEventStoreConnection _client;
        private readonly string _stream;
        private readonly CancellationToken _token;
        private readonly JsonSerializerSettings _settings;
        private readonly Compression _compress;

        private EventStoreCatchUpSubscription _subscription;

        public bool Live { get; private set; }
        public string Id => $"{_client.Settings.GossipSeeds[0].EndPoint.Address}.SNAP";

        private bool _disposed;

        public CatchupClient(Action<string, ISnapshot> onSnapshot, IEventStoreConnection client, string stream, CancellationToken token, JsonSerializerSettings settings, Compression compress)
        {
            _onSnapshot = onSnapshot;
            _client = client;
            _stream = stream;
            _token = token;
            _settings = settings;
            _compress = compress;
            _snapshots = new SortedDictionary<string, IWritableEvent>();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subscription.Stop(TimeSpan.FromSeconds(30));
        }

        private void EventAppeared(EventStoreCatchUpSubscription sub, ResolvedEvent e)
        {
            _token.ThrowIfCancellationRequested();

            Logger.Write(LogLevel.Debug,
                () =>
                        $"Snapshot appeared {e.Event.EventId} stream [{e.Event.EventStreamId}] number {e.Event.EventNumber} projection event number {e.OriginalEventNumber}");

            // Don't care about metadata streams
            if (e.Event.EventStreamId[0] == '$')
                return;

            Snapshots.Increment();

            // Todo: dont like putting serialization stuff here
            var metadata = e.Event.Metadata;
            var data = e.Event.Data;

            var descriptor = metadata.Deserialize(_settings);

            if (descriptor.Compressed)
                data = data.Decompress();

            var payload = data.Deserialize(e.Event.EventType, _settings);
            
            var snapshot = new Snapshot
            {
                EntityType = descriptor.EntityType,
                Bucket = descriptor.Bucket,
                StreamId = descriptor.StreamId,
                Timestamp = descriptor.Timestamp,
                Version = descriptor.Version,
                Payload = payload
            };

            _onSnapshot(e.Event.EventStreamId, snapshot);

        }

        private void SubscriptionDropped(EventStoreCatchUpSubscription sub, SubscriptionDropReason reason, Exception ex)
        {
            Live = false;
            Logger.Write(LogLevel.Info, () => $"Disconnected from subscription.  Reason: {reason} Exception: {ex}");

            if (reason == SubscriptionDropReason.UserInitiated) return;

            Task.Run(async () =>
            {
                // Restart
                try
                {
                    await Connect().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }, _token).Wait(_token);
        }
        public async Task Connect()
        {
            Logger.Write(LogLevel.Info,
                () =>
                        $"Connecting to snapshot stream [{_stream}] on client {_client.Settings.GossipSeeds[0].EndPoint.Address}");

            // Subscribe to the end
            var lastEvent =
                await _client.ReadStreamEventsBackwardAsync(_stream, StreamPosition.End, 1, true).ConfigureAwait(false);
            
            var settings = new CatchUpSubscriptionSettings(100, 5, Logger.IsDebugEnabled, true);

            var startingNumber = 0;
            if (lastEvent.Status == SliceReadStatus.Success)
                startingNumber = lastEvent.Events[0].OriginalEventNumber;

            _subscription = _client.SubscribeToStreamFrom(_stream,
                startingNumber,
                settings,
                eventAppeared: EventAppeared,
                subscriptionDropped: SubscriptionDropped);
            Live = true;
        }

        public Task<IWritableEvent> Retreive(string stream)
        {
            IWritableEvent snapshot;
            if (!_snapshots.TryGetValue(stream, out snapshot))
                snapshot = null;
            return Task.FromResult(snapshot);
        }

    }
}
