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
using EventStore.ClientAPI.Exceptions;
using Metrics;
using Newtonsoft.Json;
using NServiceBus.Logging;
using Timer = Metrics.Timer;

namespace Aggregates.Internal
{
    class CatchupClient : IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("CatchupClient");
        private static readonly Counter Snapshots = Metric.Counter("Snapshots Seen", Unit.Items, tags: "debug");

        // Todo: config option for "most snapshots stored" and a LRU cache?
        private readonly SortedDictionary<string, IFullEvent> _snapshots;
        private readonly Action<string, long, ISnapshot> _onSnapshot;
        private readonly IEventStoreConnection _client;
        private readonly string _stream;
        private readonly CancellationToken _token;
        private readonly JsonSerializerSettings _settings;
        private readonly Compression _compress;

        private EventStoreCatchUpSubscription _subscription;

        public bool Live { get; private set; }
        public string Id => $"{_client.Settings.GossipSeeds[0].EndPoint.Address}.SNAP";

        private bool _disposed;

        public CatchupClient(Action<string, long, ISnapshot> onSnapshot, IEventStoreConnection client, string stream,
            CancellationToken token, JsonSerializerSettings settings, Compression compress)
        {
            _onSnapshot = onSnapshot;
            _client = client;
            _stream = stream;
            _token = token;
            _settings = settings;
            _compress = compress;
            _snapshots = new SortedDictionary<string, IFullEvent>();

            _client.Connected += _client_Connected;
        }

        private void _client_Connected(object sender, ClientConnectionEventArgs e)
        {
            Task.Run(Connect, _token);
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
                () => $"Event appeared {e.Event?.EventId ?? Guid.Empty} in snapshot subscription stream [{e.Event?.EventStreamId ?? ""}] number {e.Event?.EventNumber ?? -1} projection event number {e.OriginalEventNumber}");

            // Don't care about metadata streams
            if (e.Event == null || e.Event.EventStreamId[0] == '$')
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

            _onSnapshot(e.Event.EventStreamId, e.Event.EventNumber, snapshot);

        }

        private void SubscriptionDropped(EventStoreCatchUpSubscription sub, SubscriptionDropReason reason, Exception ex)
        {
            Live = false;
            Logger.Write(LogLevel.Info, () => $"Disconnected from subscription.  Reason: {reason} Exception: {ex}");

            if (reason == SubscriptionDropReason.UserInitiated) return;

            // Task.Run(Connect, _token);
        }

        public async Task Connect()
        {
            Logger.Write(LogLevel.Info,
                () => $"Connecting to snapshot stream [{_stream}] on client {_client.Settings.GossipSeeds[0].EndPoint.Address}");

            // Subscribe to the end
            var lastEvent =
                await _client.ReadStreamEventsBackwardAsync(_stream, StreamPosition.End, 1, true).ConfigureAwait(false);

            var settings = new CatchUpSubscriptionSettings(100, 5, Logger.IsDebugEnabled, true);

            var startingNumber = 0L;
            if (lastEvent.Status == SliceReadStatus.Success)
                startingNumber = lastEvent.Events[0].OriginalEventNumber;

            // ReadStreamEventsBackward is async, which means at this point we'll be processing on the client's OperationQueue
            // put a delay here so the queue can get back to work, SubscribeToStreamFrom is sync meaning if we jump
            // right into it the OperationsQueue will deadlock
            while (!Live)
            {
                await Task.Delay(500, _token).ConfigureAwait(false);

                try
                {
                    _subscription = _client.SubscribeToStreamFrom(_stream,
                        startingNumber,
                        settings,
                        eventAppeared: EventAppeared,
                        subscriptionDropped: SubscriptionDropped);
                    Logger.Write(LogLevel.Info,
                        () => $"Connected to snapshot stream [{_stream}] on client {_client.Settings.GossipSeeds[0].EndPoint.Address}");
                    Live = true;
                }
                catch (OperationTimedOutException){}
            }
        }

        public Task<IFullEvent> Retreive(string stream)
        {
            IFullEvent snapshot;
            if (!_snapshots.TryGetValue(stream, out snapshot))
                snapshot = null;
            return Task.FromResult(snapshot);
        }

    }
}
