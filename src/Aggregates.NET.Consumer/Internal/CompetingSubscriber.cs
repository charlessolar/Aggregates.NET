using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
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
        private readonly IBuilder _builder;
        private readonly IEventStoreConnection _client;
        private readonly IManageCompetes _competes;
        private readonly ReadOnlySettings _settings;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly HashSet<Int32> _buckets;
        private readonly IDictionary<Int32, long> _seenBuckets;
        private readonly System.Threading.Timer _bucketHeartbeats;
        private readonly System.Threading.Timer _bucketPause;
        private readonly Int32 _bucketCount;
        private Boolean _pauseOnFreeBucket;
        private Boolean _pausedArmed;
        private Boolean _paused;
        private Int32? _adopting;
        private Int64? _adoptingPosition;
        private Object _lock = new object();

        public Boolean ProcessingLive { get; set; }
        public Action<String, Exception> Dropped { get; set; }

        public CompetingSubscriber(IBuilder builder, IEventStoreConnection client, IManageCompetes competes, ReadOnlySettings settings, IMessageMapper mapper)
        {
            _builder = builder;
            _client = client;
            _competes = competes;
            _settings = settings;
            _buckets = new HashSet<Int32>();
            _seenBuckets = new Dictionary<Int32, long>();
            _bucketCount = _settings.Get<Int32>("BucketCount");
            _pauseOnFreeBucket = _settings.Get<Boolean>("PauseOnFreeBuckets");
            _paused = true;

            _jsonSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };

            var period = TimeSpan.FromSeconds(_settings.Get<Int32>("BucketHeartbeats"));
            _bucketHeartbeats = new System.Threading.Timer((state) =>
            {
                Logger.Write(LogLevel.Debug, "Processing compete heartbeats");
                var handledBuckets = _settings.Get<Int32>("BucketsHandled");
                var expiration = _settings.Get<Int32>("BucketExpiration");

                var consumer = (CompetingSubscriber)state;
                var endpointName = consumer._settings.EndpointName();

                var notSeenBuckets = new HashSet<Int32>(consumer._buckets);
                IDictionary<Int32, long> seenBuckets;
                lock(_lock)
                {
                    seenBuckets = new Dictionary<Int32, long>(consumer._seenBuckets);
                    consumer._seenBuckets.Clear();
                }

                foreach (var seen in seenBuckets)
                {
                    if (consumer._buckets.Contains(seen.Key))
                    {
                        try
                        {
                            Logger.Write(LogLevel.Debug, () => $"Heartbeating bucket {seen.Key} position {seen.Value}");
                            consumer._competes.Heartbeat(endpointName, seen.Key, DateTime.UtcNow, seen.Value);
                        }
                        catch (DiscriminatorException)
                        {
                            // someone else took over the bucket
                            consumer._buckets.Remove(seen.Key);
                            Logger.Write(LogLevel.Info, () => $"Lost claim on bucket {seen.Key}.  Total claimed: {consumer._buckets.Count}/{handledBuckets}");
                        }
                        notSeenBuckets.Remove(seen.Key);
                    }
                    else if (!consumer._adopting.HasValue && consumer._buckets.Count < handledBuckets)
                    {
                        var lastBeat = consumer._competes.LastHeartbeat(endpointName, seen.Key);
                        if (lastBeat.HasValue && (DateTime.UtcNow - lastBeat.Value).TotalSeconds > expiration)
                        {
                            Logger.Write(LogLevel.Debug, () => $"Last beat on bucket {seen.Key} is {lastBeat} - it is {(DateTime.UtcNow - lastBeat.Value).TotalSeconds} seconds old, adopting...");
                            // We saw new events but the consumer for this bucket has died, so we will adopt its bucket
                            AdoptBucket(consumer, endpointName, seen.Key);
                        }
                    }
                    else if (consumer._adopting == seen.Key)
                    {
                        try
                        {
                            Logger.Write(LogLevel.Debug, () => $"Heartbeating adopted bucket {seen.Key} position {consumer._adoptingPosition.Value}");
                            consumer._competes.Heartbeat(endpointName, seen.Key, DateTime.UtcNow, consumer._adoptingPosition.Value);
                        }
                        catch (DiscriminatorException)
                        {
                            // someone else took over the bucket
                            consumer._adopting = null;
                            consumer._adoptingPosition = null;
                            Logger.Write(LogLevel.Info, () => $"Lost claim on adopted bucket {seen.Key}.  Total claimed: {consumer._buckets.Count}/{handledBuckets}");
                        }
                        notSeenBuckets.Remove(seen.Key);
                    }
                }

                var expiredBuckets = new List<Int32>();
                // Heartbeat the buckets we haven't seen but are still watching
                foreach (var bucket in notSeenBuckets)
                {
                    try
                    {
                        Logger.Write(LogLevel.Debug, () => $"Heartbeating unseen bucket {bucket}");
                        consumer._competes.Heartbeat(endpointName, bucket, DateTime.UtcNow);
                    }
                    catch (DiscriminatorException)
                    {
                        // someone else took over the bucket
                        consumer._buckets.Remove(bucket);
                        Logger.Write(LogLevel.Info, () => $"Lost claim on bucket {bucket}.  Total claimed: {consumer._buckets.Count}/{handledBuckets}");
                    }
                }

            }, this, period, period);

            if (!_pauseOnFreeBucket) return;

            _bucketPause = new System.Threading.Timer(state =>
            {
                var consumer = (CompetingSubscriber)state;
                var endpointName = consumer._settings.EndpointName();
                var expiration = _settings.Get<Int32>("BucketExpiration");

                var openBuckets = consumer._bucketCount;

                // Check that all buckets are being watched

                Parallel.For(0, consumer._bucketCount, idx =>
                {
                    var lastBeat = consumer._competes.LastHeartbeat(endpointName, idx);
                    if (lastBeat.HasValue && (DateTime.UtcNow - lastBeat.Value).TotalSeconds < expiration)
                        Interlocked.Decrement(ref openBuckets);
                });

                if (openBuckets != 0 && _pausedArmed == false)
                {
                    Logger.Write(LogLevel.Warn, () => $"Detected {openBuckets} free buckets - pause ARMED");
                    _pausedArmed = true;
                }
                else if(openBuckets != 0 && _pausedArmed == true)
                {
                    Logger.Write(LogLevel.Warn, () => $"Detected {openBuckets} free buckets - PAUSING");
                    _paused = true;
                }
                else
                {
                    _pausedArmed = false;
                    _paused = false;
                }
                

            }, this, TimeSpan.FromSeconds(period.Seconds / 2), period);
            // Set open check to not run at the same moment as heartbeating
        }
        public void Dispose()
        {
            this._bucketHeartbeats.Dispose();
        }

        private static void AdoptBucket(CompetingSubscriber consumer, String endpoint, Int32 bucket)
        {
            var handledBuckets = consumer._settings.Get<Int32>("BucketsHandled");
            var readSize = consumer._settings.Get<Int32>("ReadSize");

            Logger.Write(LogLevel.Info, () => $"Discovered orphaned bucket {bucket}.. adopting");
            if(!consumer._competes.Adopt(endpoint, bucket, DateTime.UtcNow))
            {
                Logger.Write(LogLevel.Info, () => $"Failed to adopt bucket {bucket}.. maybe next time");
                return;
            }
            consumer._adopting = bucket;
            var lastPosition = consumer._competes.LastPosition(endpoint, bucket);
            consumer._adoptingPosition = lastPosition;

            var bus = consumer._builder.Build<IMessageSession>();
            var settings = new CatchUpSubscriptionSettings(readSize * readSize, readSize, false, false);
            consumer._client.SubscribeToAllFrom(new Position(lastPosition, lastPosition), settings, (subscription, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;
                var eventBucket = Math.Abs(e.OriginalStreamId.GetHashCode() % consumer._bucketCount);
                if (eventBucket != bucket) return;

                Logger.Write(LogLevel.Debug, () => $"Adopted event appeared position {e.OriginalPosition?.CommitPosition}... processing - bucket {bucket}");
                if (!e.OriginalPosition.HasValue) return;

                var descriptor = e.Event.Metadata.Deserialize(consumer._jsonSettings);
                if (descriptor == null) return;

                var data = e.Event.Data.Deserialize(e.Event.EventType, consumer._jsonSettings);
                if (data == null) return;

                consumer._adoptingPosition = e.OriginalPosition?.CommitPosition ?? consumer._adoptingPosition;

                var options = new SendOptions();

                options.RouteToThisInstance();
                options.SetHeader("CommitPosition", e.OriginalPosition?.CommitPosition.ToString());
                options.SetHeader("EntityType", descriptor.EntityType);
                options.SetHeader("Version", descriptor.Version.ToString());
                options.SetHeader("Timestamp", descriptor.Timestamp.ToString());
                options.SetHeader("Adopting", "1");
                foreach (var header in descriptor.Headers)
                    options.SetHeader(header.Key, header.Value);

                try
                {
                    bus.Send(data, options).Wait();
                }
                catch (SubscriptionCanceled)
                {
                    subscription.Stop();
                    throw;
                }
            }, liveProcessingStarted: (sub) =>
            {
                sub.Stop();
                consumer._buckets.Add(bucket);
                consumer._adopting = null;
                consumer._adoptingPosition = null;
                Logger.Write(LogLevel.Info, () => $"Successfully adopted bucket {bucket}.  Total claimed: {consumer._buckets.Count}/{handledBuckets}");
            }, subscriptionDropped: (subscription, reason, e) =>
            {
                if (reason == SubscriptionDropReason.UserInitiated) return;
                consumer._adopting = null;
                consumer._adoptingPosition = null;
                subscription.Stop();
                Logger.Write(LogLevel.Warn, () => $"While adopting bucket {bucket} the Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
            });
        }

        public void SubscribeToAll(IMessageSession bus, String endpoint)
        {
            // To support HA simply save IManageCompetes data to a different db, in this way we can make clusters of consumers
            var handledBuckets = _settings.Get<Int32>("BucketsHandled");
            var readSize = _settings.Get<Int32>("ReadSize");
            
            // Start competing subscribers from the start, if they are picking up a new bucket they need to start from the begining
            Logger.Write(LogLevel.Info, () => $"Endpoint '{endpoint}' subscribing to all events from START");
            var settings = new CatchUpSubscriptionSettings(readSize * readSize, readSize, false, false);
            _client.SubscribeToAllFrom(Position.Start, settings, (subscription, e) =>
            {
                // Unsure if we need to care about events from eventstore currently
                if (!e.Event.IsJson) return;


                var bucket = Math.Abs(e.OriginalStreamId.GetHashCode() % _bucketCount);

                if (!e.OriginalPosition.HasValue) return;

                if (!_buckets.Contains(bucket))
                {
                    // If we are already handling enough buckets, or we've seen (and tried to claim) it before, ignore
                    if (_buckets.Count >= handledBuckets || _seenBuckets.ContainsKey(bucket))
                    {
                        Logger.Write(LogLevel.Debug, () => $"Event appeared position {e.OriginalPosition?.CommitPosition}... skipping");
                        lock(_lock) _seenBuckets[bucket] = e.OriginalPosition.Value.CommitPosition;
                        return;
                    }
                    else
                    {
                        Logger.Write(LogLevel.Debug, () => $"Attempting to claim bucket {bucket}");
                        // Returns true if it claimed the bucket
                        if (_competes.CheckOrSave(endpoint, bucket, e.OriginalPosition.Value.CommitPosition))
                        {
                            _buckets.Add(bucket);
                            Logger.Write(LogLevel.Info, () => $"Claimed bucket {bucket}.  Total claimed: {_buckets.Count}/{handledBuckets}");
                        }
                        else
                        {
                            lock(_lock) _seenBuckets[bucket] = e.OriginalPosition.Value.CommitPosition;
                            return;
                        }
                    }
                }
                Logger.Write(LogLevel.Debug, () => $"Event appeared position {e.OriginalPosition?.CommitPosition}... processing - bucket {bucket}");
                lock(_lock) _seenBuckets[bucket] = e.OriginalPosition.Value.CommitPosition;


                var descriptor = e.Event.Metadata.Deserialize(_jsonSettings);
                if (descriptor == null) return;

                var data = e.Event.Data.Deserialize(e.Event.EventType, _jsonSettings);

                // Data is null for certain irrelevant eventstore messages (and we don't need to store position or snapshots)
                if (data == null) return;

                var options = new SendOptions();

                options.RouteToThisInstance();
                options.SetHeader("CommitPosition", e.OriginalPosition?.CommitPosition.ToString());
                options.SetHeader("EntityType", descriptor.EntityType);
                options.SetHeader("Version", descriptor.Version.ToString());
                options.SetHeader("Timestamp", descriptor.Timestamp.ToString());
                foreach (var header in descriptor.Headers)
                    options.SetHeader(header.Key, header.Value);

                while (_paused)
                    Thread.Sleep(50);

                try
                {
                    bus.Send(data, options).Wait();
                }
                catch (SubscriptionCanceled)
                {
                    subscription.Stop();
                    throw;
                }

            }, liveProcessingStarted: (_) =>
            {
                Logger.Write(LogLevel.Info, "Live processing started");
                ProcessingLive = true;
            }, subscriptionDropped: (_, reason, e) =>
            {
                Logger.Write(LogLevel.Warn, () => $"Subscription dropped for reason: {reason}.  Exception: {e?.Message ?? "UNKNOWN"}");
                ProcessingLive = false;
                if (Dropped != null)
                    Dropped.Invoke(reason.ToString(), e);
            });
        }

    }
}
