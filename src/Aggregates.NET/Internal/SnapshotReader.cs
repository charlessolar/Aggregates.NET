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
        // Store snapshots as strings, so that each request returns a new object which doesn't need to be deep copied
        private static readonly ConcurrentDictionary<string, Tuple<DateTime, string>> Snapshots = new ConcurrentDictionary<string, Tuple<DateTime, string>>();
        private static readonly ConcurrentDictionary<string, long> TruncateBefore = new ConcurrentDictionary<string, long>();

        private static CancellationTokenSource Cancellation = new CancellationTokenSource();
        
        private static Task Truncate = null;
        private static Task SnapshotExpiration = null;

        private string _endpoint;
        private Version _version;

        private readonly IMetrics _metrics;
        private readonly IStoreEvents _store;
        private readonly IEventStoreConsumer _consumer;
        private readonly IMessageSerializer _serializer;

        public SnapshotReader(IMetrics metrics, IStoreEvents store, IEventStoreConsumer consumer, IMessageSerializer serializer)
        {
            _metrics = metrics;
            _store = store;
            _consumer = consumer;
            _serializer = serializer;
        }

        public async Task Setup(string endpoint, Version version)
        {
            _endpoint = endpoint;
            _version = version;
            await _consumer.EnableProjection("$by_category").ConfigureAwait(false);

            SnapshotExpiration = Timer.Repeat((state) =>
            {
                var metrics = state as IMetrics;

                var expired = Snapshots.Where(x => (DateTime.UtcNow - x.Value.Item1) > TimeSpan.FromMinutes(5)).Select(x => x.Key)
                    .ToList();

                Tuple<DateTime, string> temp;
                foreach (var key in expired)
                    if (Snapshots.TryRemove(key, out temp))
                        metrics.Decrement("Snapshots Stored", Unit.Items);

                return Task.CompletedTask;
            }, _metrics, TimeSpan.FromMinutes(5), Cancellation.Token, "expires snapshots from the cache");

            Truncate= Timer.Repeat(async (state) =>
            {
                // Writes truncateBefore metadata to snapshot streams to let ES know it can delete old snapshots
                // its done here so that we actually get the snapshot before its deleted

                var store = state as IStoreEvents;

                var truncates = TruncateBefore.Keys.ToList();

                await truncates.WhenAllAsync(async x =>
                {
                    long tb;
                    if (!TruncateBefore.TryRemove(x, out tb))
                        return;

                    try
                    {
                        await store.WriteMetadata(x, truncateBefore: tb).ConfigureAwait(false);
                    }
                    catch
                    {
                        // doesn't matter if metadata fails to write - we'll try again later
                    }
                }).ConfigureAwait(false);
            }, _store, TimeSpan.FromMinutes(5), Cancellation.Token, "snapshot truncate before");
        }

        public Task Connect()
        {
            // by_category projection of all events in category "SNAPSHOT"
            var stream = $"$ce-{StreamTypes.Snapshot}";
            return Reconnect(stream);

        }
        public Task Shutdown()
        {
            Cancellation.Cancel();
            return Task.CompletedTask;
        }

        private Task Reconnect(string stream)
        {
            return _consumer.SubscribeToStreamEnd(stream, Cancellation.Token, onEvent, () => Reconnect(stream));
        }

        private Task onEvent(string stream, long position, IFullEvent e)
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

            Logger.DebugEvent("GotSnapshot", "[{Stream:l}] bucket [{Bucket:l}] entity [{EntityType:l}] version {Version}", snapshot.StreamId, snapshot.Bucket, snapshot.EntityType, snapshot.Version);
            Snapshots.AddOrUpdate(stream, (key) =>
            {
                _metrics.Increment("Snapshots Stored", Unit.Items);
                return new Tuple<DateTime, string>(DateTime.UtcNow, _serializer.Serialize(snapshot).AsString());
            }, (key, existing) =>
            {
                TruncateBefore[key] = position;
                return new Tuple<DateTime, string>(DateTime.UtcNow, _serializer.Serialize(snapshot).AsString());
            });
            return Task.CompletedTask;
        }

        public Task<ISnapshot> Retreive(string stream)
        {
            Tuple<DateTime, string> snapshot;
            if (!Snapshots.TryGetValue(stream, out snapshot))
                return Task.FromResult((ISnapshot)null);

            // Update timestamp so snapshot doesn't expire
            // let all snapshots eventually expire
            //Snapshots.TryUpdate(stream, new Tuple<DateTime, ISnapshot>(DateTime.UtcNow, snapshot.Item2), snapshot);

            // Explanation:
            // Snapshots are stored as strings so that each retreive creates a new object (deep copy in C# doesn't work very well)
            // snapshots have a 'Snapshot' property which is supposed to be a clean copy of the state for use in event handlers
            // think: 
            //
            // void Handle(Events.FooBar e)
            //    // this.Snapshot is the unmodified state 
            //
            // to support that unmodified state we need to deserialize the snapshot twice - once for the state
            // second for the snapshot property on the state.
            // Todo: is there a way to mark the whole object as readonly?
            var result = (ISnapshot)_serializer.Deserialize<Snapshot>(snapshot.Item2.AsByteArray());
            (result.Payload as IState).Snapshot = (IState)(_serializer.Deserialize<Snapshot>(snapshot.Item2.AsByteArray()).Payload);
            return Task.FromResult(result);
        }

        public void Dispose()
        {
            Snapshots.Clear();
            TruncateBefore.Clear();
        }
    }

}
