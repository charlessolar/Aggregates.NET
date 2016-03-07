using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Aggregates
{
    /// <summary>
    /// Keeps track of the domain events it handles and imposes a limit on the amount of events to process (allowing other instances to process others)
    /// Used for load balancing
    /// </summary>
    public class CompetingSubscriber : IEventSubscriber, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CompetingSubscriber));
        private readonly IEventStoreConnection _client;
        private readonly IPersistCheckpoints _checkpoints;
        private readonly IManageCompetes _competes;
        private readonly IDispatcher _dispatcher;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly HashSet<Int32> _buckets;
        private readonly ConcurrentDictionary<Int32, long> _seenBuckets;
        private readonly System.Threading.Timer _bucketChecker;
        private readonly Int32 _bucketCount;

        public CompetingSubscriber(IEventStoreConnection client, IPersistCheckpoints checkpoints, IManageCompetes competes, IDispatcher dispatcher, ReadOnlySettings settings, JsonSerializerSettings jsonSettings)
        {
            _client = client;
            _checkpoints = checkpoints;
            _competes = competes;
            _dispatcher = dispatcher;
            _settings = settings;
            _jsonSettings = jsonSettings;
            _buckets = new HashSet<Int32>();
            _seenBuckets = new ConcurrentDictionary<Int32, long>();
            _bucketCount = _settings.Get<Int32>("BucketCount");

            var period = TimeSpan.FromSeconds(_settings.Get<Int32>("BucketHeartbeats"));
            _bucketChecker = new System.Threading.Timer((state) =>
            {
                Logger.Debug("Processing compete heartbeats");
                var handledBuckets = _settings.Get<Int32>("BucketsHandled");

                var consumer = (CompetingSubscriber)state;
                var endpoint = consumer._settings.EndpointName();
                
                var seenBuckets = new Dictionary<Int32, long>(consumer._seenBuckets);
                consumer._seenBuckets.Clear();

                foreach (var seen in seenBuckets)
                {
                    if (consumer._buckets.Contains(seen.Key))
                        consumer._competes.Heartbeat(endpoint, seen.Key, DateTime.UtcNow, seen.Value);
                    
                }
                
                
            }, this, period, period);
        }
        public void Dispose()
        {
            this._bucketChecker.Dispose();
        }

        public void SubscribeToAll(String endpoint)
        {
            var saved = _checkpoints.Load(endpoint);
            // To support HA simply save IManageCompetes data to a different db, in this way we can make clusters of consumers
            var handledBuckets = _settings.Get<Int32>("BucketsHandled");

            Logger.InfoFormat("Endpoint '{0}' subscribing to all events from position '{1}'", endpoint, saved);
            _client.SubscribeToAllFrom(saved, false, (subscription, e) =>
            {
                Thread.CurrentThread.Rename("Eventstore");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);

                if (descriptor == null) return;

                var bucket = Math.Abs(e.OriginalStreamId.GetHashCode() % _bucketCount);

                
                if (e.OriginalPosition.HasValue)
                {
                    _seenBuckets[bucket] = e.OriginalPosition.Value.CommitPosition;

                    if (!_buckets.Contains(bucket))
                    {
                        if (_buckets.Count >= handledBuckets)
                            return;
                        else
                        {
                            // Returns true if it claimed the bucket
                            if (_competes.CheckOrSave(endpoint, bucket, e.OriginalPosition.Value.CommitPosition))
                            {
                                _buckets.Add(bucket);
                                Logger.InfoFormat("Claimed bucket {0}.  Total claimed: {1}/{2}", bucket, _buckets.Count, handledBuckets);
                            }
                            else
                                return;
                        }
                    }
                }

                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;
                
                _dispatcher.Dispatch(data, descriptor, e.OriginalPosition?.CommitPosition);

            }, liveProcessingStarted: (_) =>
            {
                Logger.Info("Live processing started");
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("Subscription dropped for reason: {0}.  Exception: {1}", reason, e);
            }, readBatchSize: 100);
        }

    }
}
