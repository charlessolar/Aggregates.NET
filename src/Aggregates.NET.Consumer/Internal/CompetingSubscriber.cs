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


namespace Aggregates.Internal
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
        private Boolean _adopting;

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
            _adopting = false;

            var period = TimeSpan.FromSeconds(_settings.Get<Int32>("BucketHeartbeats"));
            _bucketChecker = new System.Threading.Timer((state) =>
            {
                Logger.Debug("Processing compete heartbeats");
                var handledBuckets = _settings.Get<Int32>("BucketsHandled");
                var expiration = _settings.Get<Int32>("BucketExpiration");

                var consumer = (CompetingSubscriber)state;
                var endpoint = consumer._settings.EndpointName();

                var notSeenBuckets = new HashSet<Int32>(consumer._buckets);
                var seenBuckets = new Dictionary<Int32, long>(consumer._seenBuckets);
                consumer._seenBuckets.Clear();

                foreach (var seen in seenBuckets)
                {
                    if (consumer._buckets.Contains(seen.Key))
                    {
                        consumer._competes.Heartbeat(endpoint, seen.Key, DateTime.UtcNow, seen.Value);
                        notSeenBuckets.Remove(seen.Key);
                    }
                    else if (!consumer._adopting && consumer._buckets.Count < handledBuckets)
                    {
                        var lastBeat = consumer._competes.LastHeartbeat(endpoint, seen.Key);
                        if (lastBeat.HasValue && (DateTime.UtcNow - lastBeat.Value).TotalSeconds > expiration)
                        {
                            // We saw new events but the consumer for this bucket has died, so we will adopt its bucket
                            AdoptBucket(consumer, endpoint, seen.Key);
                            break;
                        }
                    }
                }

                var expiredBuckets = new List<Int32>();
                // Check that each bucket we are processing is still alive
                foreach (var bucket in notSeenBuckets)
                {
                    var lastBeat = consumer._competes.LastHeartbeat(endpoint, bucket);
                    if (lastBeat.HasValue && (DateTime.UtcNow - lastBeat.Value).TotalSeconds > expiration)
                        expiredBuckets.Add(bucket);
                }
                expiredBuckets.ForEach(x =>
                {
                    consumer._buckets.Remove(x);
                    Logger.InfoFormat("Detected and removed expired bucket {0}.  Total claimed: {1}/{2}", x, consumer._buckets.Count, consumer._bucketCount);
                });
                
            }, this, period, period);
        }
        public void Dispose()
        {
            this._bucketChecker.Dispose();
        }

        private static void AdoptBucket(CompetingSubscriber consumer, String endpoint, Int32 bucket)
        {
            var readSize = consumer._settings.Get<Int32>("ReadSize");
            Logger.InfoFormat("Discovered orphaned bucket {0}.. adopting", bucket);
            consumer._adopting = true;
            var lastPosition = consumer._competes.LastPosition(endpoint, bucket);
            consumer._client.SubscribeToAllFrom(new Position(lastPosition, lastPosition), false, (subscription, e) =>
            {
                Thread.CurrentThread.Rename("Eventstore");
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;

                var descriptor = e.Event.Metadata.Deserialize(consumer._jsonSettings);
                if (descriptor == null) return;

                var eventBucket = Math.Abs(e.OriginalStreamId.GetHashCode() % consumer._bucketCount);
                if (eventBucket != bucket) return;
                                
                var data = e.Event.Data.Deserialize(e.Event.EventType, consumer._jsonSettings);
                if (data == null) return;
                
                consumer._dispatcher.Dispatch(data, descriptor);

            }, liveProcessingStarted: (sub) =>
            {
                consumer._buckets.Add(bucket);
                consumer._adopting = false;
                sub.Stop();
                Logger.InfoFormat("Successfully adopted bucket {0}", bucket);
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.WarnFormat("While adopting bucket {0} the subscription dropped for reason: {1}.  Exception: {2}", bucket, reason, e);
            }, readBatchSize: readSize);
        }

        public void SubscribeToAll(String endpoint)
        {
            var saved = _checkpoints.Load(endpoint);
            // To support HA simply save IManageCompetes data to a different db, in this way we can make clusters of consumers
            var handledBuckets = _settings.Get<Int32>("BucketsHandled");
            var readSize = _settings.Get<Int32>("ReadSize");

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
            }, readBatchSize: readSize);
        }

    }
}
