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
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.Projections;
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
        private static readonly Counter StoredSnapshots = Metric.Counter("Snapshots Stored", Unit.Items, tags: "debug");

        private static readonly ConcurrentDictionary<string, ISnapshot> Snapshots = new ConcurrentDictionary<string, ISnapshot>();
        private static readonly ConcurrentDictionary<string, int> TruncateBefore = new ConcurrentDictionary<string, int>();

        private static Task Truncate;

        private CancellationTokenSource _cancelation;
        private string _endpoint;

        private readonly IStoreEvents _store;
        private readonly JsonSerializerSettings _settings;
        private readonly IEventStoreConnection[] _connections;
        private readonly Compression _compress;

        private CatchupClient[] _clients;


        private bool _disposed;

        public SnapshotReader(IStoreEvents store, IMessageMapper mapper, IEventStoreConnection[] connections, Compression compress)
        {
            _store = store;
            _connections = connections;
            _compress = compress;
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Binder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper)
            };
            if (Truncate == null)
            {
                Truncate = Timer.Repeat(async (state) =>
                {
                    var eventstore = state as IStoreEvents;

                    var truncates = TruncateBefore.Keys.ToList();

                    await truncates.SelectAsync(async x =>
                    {
                        int tb;
                        if (!TruncateBefore.TryRemove(x, out tb))
                            return;

                        try
                        {
                            await eventstore.WriteMetadata(x, truncateBefore: tb).ConfigureAwait(false);
                        }
                        catch {}
                    });
                }, store, TimeSpan.FromMinutes(5), "snapshot truncate before");
            }
        }

        public async Task Setup(string endpoint)
        {
            _endpoint = endpoint;

            // Setup snapshot projection
            foreach (var connection in _connections)
            {
                if (connection.Settings.GossipSeeds == null || !connection.Settings.GossipSeeds.Any())
                    throw new ArgumentException(
                        "Eventstore connection settings does not contain gossip seeds (even if single host call SetGossipSeedEndPoints and SetClusterGossipPort)");

                var manager = new ProjectionsManager(connection.Settings.Log,
                    new IPEndPoint(connection.Settings.GossipSeeds[0].EndPoint.Address,
                        connection.Settings.ExternalGossipPort), TimeSpan.FromSeconds(5));

                await manager.EnableAsync("$by_category", connection.Settings.DefaultUserCredentials).ConfigureAwait(false);

            }
        }

        public async Task Subscribe(CancellationToken cancelToken)
        {
            // by_category projection of all events in category "SNAPSHOT"
            var stream = $"$ce-SNAPSHOT";

            _cancelation = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);

            _clients = new CatchupClient[_connections.Count()];
            for (var i = 0; i < _connections.Count(); i++)
            {
                var connection = _connections.ElementAt(i);

                var clientCancelSource = CancellationTokenSource.CreateLinkedTokenSource(_cancelation.Token);

                connection.Closed += (object s, ClientClosedEventArgs args) =>
                {
                    Logger.Info($"Eventstore disconnected - shutting down snapshot subscription");
                    clientCancelSource.Cancel();
                };

                _clients[i] = new CatchupClient(onSnapshot, connection, stream, clientCancelSource.Token, _settings, _compress);
                await _clients[i].Connect().ConfigureAwait(false);
            }
        }

        private void onSnapshot(string stream, int position, ISnapshot snapshot)
        {
            Logger.Write(LogLevel.Debug, () => $"Got snapshot stream [{stream}] version {snapshot.Version}");
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
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancelation.Cancel();
            foreach (var client in _clients)
                client.Dispose();
        }
    }

}
