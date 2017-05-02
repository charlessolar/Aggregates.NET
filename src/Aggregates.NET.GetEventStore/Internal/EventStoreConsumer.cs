using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.Projections;
using Newtonsoft.Json;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;

namespace Aggregates.Internal
{
    internal class EventStoreConsumer : IEventStoreConsumer, IDisposable
    {
        private static readonly ILog Logger = LogManager.GetLogger("EventStoreConsumer");
        
        private readonly IEventStoreConnection[] _clients;
        private readonly JsonSerializerSettings _settings;
        private readonly int _readSize;
        private readonly bool _extraStats;
        private readonly object _subLock;
        private readonly List<EventStoreCatchUpSubscription> _subscriptions;
        private readonly List<EventStorePersistentSubscriptionBase> _persistentSubs;
        private readonly ConcurrentDictionary<Guid, Tuple<EventStorePersistentSubscriptionBase, Guid>> _outstandingEvents;
        private bool _disposed;

        public EventStoreConsumer(IMessageMapper mapper, IEventStoreConnection[] clients, int readSize, bool extraStats)
        {
            _clients = clients;

            _readSize = readSize;
            _extraStats = extraStats;
            _subLock = new object();
            _subscriptions = new List<EventStoreCatchUpSubscription>();
            _persistentSubs = new List<EventStorePersistentSubscriptionBase>();
            _outstandingEvents = new ConcurrentDictionary<Guid, Tuple<EventStorePersistentSubscriptionBase, Guid>>();
            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new EventSerializationBinder(mapper),
                ContractResolver = new EventContractResolver(mapper),
                Converters = new[] { new Newtonsoft.Json.Converters.StringEnumConverter() }
            };


            if (clients.Any(x => x.Settings.GossipSeeds == null || !x.Settings.GossipSeeds.Any()))
                throw new ArgumentException(
                    "Eventstore connection settings does not contain gossip seeds (even if single host call SetGossipSeedEndPoints and SetClusterGossipPort)");
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var sub in _subscriptions)
                sub.Stop(TimeSpan.FromSeconds(5));
        }



        public Task<bool> SubscribeToStreamStart(string stream, CancellationToken token, Action<string, long, IFullEvent> callback, Func<Task> disconnected)
        {
            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (var client in _clients)
            {
                Logger.Write(LogLevel.Info,
                    () => $"Subscribing to beginning of stream [{stream}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");


                var settings = new CatchUpSubscriptionSettings(1000, 50, Logger.IsDebugEnabled, true);
                var startingNumber = 0L;
                try
                {
                    var subscription = client.SubscribeToStreamFrom(stream,
                        startingNumber,
                        settings,
                        eventAppeared: (sub, e) => EventAppeared(sub, e, clientsToken.Token, callback),
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected));
                    Logger.Write(LogLevel.Info,
                        () => $"Subscribed to stream [{stream}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");
                    lock (_subLock) _subscriptions.Add(subscription);

                }
                catch (OperationTimedOutException)
                {
                    // If one fails, cancel all the others
                    clientsToken.Cancel();
                }
            }
            return Task.FromResult(!clientsToken.IsCancellationRequested);
        }

        public async Task<bool> SubscribeToStreamEnd(string stream, CancellationToken token, Action<string, long, IFullEvent> callback, Func<Task> disconnected)
        {
            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (var client in _clients)
            {
                try
                {
                    Logger.Write(LogLevel.Info,
                        () => $"Subscribing to end of stream [{stream}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");
                    // Subscribe to the end
                    var lastEvent =
                        await client.ReadStreamEventsBackwardAsync(stream, StreamPosition.End, 1, true).ConfigureAwait(false);

                    var settings = new CatchUpSubscriptionSettings(1000, 50, Logger.IsDebugEnabled, true);

                    var startingNumber = 0L;
                    if (lastEvent.Status == SliceReadStatus.Success)
                        startingNumber = lastEvent.Events[0].OriginalEventNumber;

                    var subscription = client.SubscribeToStreamFrom(stream,
                        startingNumber,
                        settings,
                        eventAppeared: (sub, e) => EventAppeared(sub, e, clientsToken.Token, callback),
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected));
                    Logger.Write(LogLevel.Info,
                        () => $"Subscribed to stream [{stream}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");
                    lock (_subLock) _subscriptions.Add(subscription);
                }
                catch (OperationTimedOutException)
                {
                    // If one fails, cancel all the others
                    clientsToken.Cancel();
                }
            }
            return !clientsToken.IsCancellationRequested;
        }

        public async Task<bool> ConnectPinnedPersistentSubscription(string stream, string group, CancellationToken token,
            Action<string, long, IFullEvent> callback, Func<Task> disconnected)
        {
            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (var client in _clients)
            {
                Logger.Write(LogLevel.Info,
                    () => $"Connecting to persistent subscription stream [{stream}] group [{group}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");


                var settings = PersistentSubscriptionSettings.Create()
                    .StartFromBeginning()
                    .WithMaxRetriesOf(10)
                    .WithReadBatchOf(_readSize)
                    .WithLiveBufferSizeOf(_readSize * 5)
                    .WithMessageTimeoutOf(TimeSpan.FromMinutes(5))
                    .CheckPointAfter(TimeSpan.FromMinutes(1))
                    .MaximumCheckPointCountOf(_readSize*5)
                    .ResolveLinkTos()
                    .WithNamedConsumerStrategy(SystemConsumerStrategies.Pinned);
                if (_extraStats)
                    settings.WithExtraStatistics();

                try
                {
                    await client.CreatePersistentSubscriptionAsync(stream, group, settings,
                        client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                    Logger.Info($"Created PINNED persistent subscription stream [{stream}] group [{group}]");
                }
                catch (InvalidOperationException)
                {
                    // Already created
                }

                try
                {
                    var subscription = await client.ConnectToPersistentSubscriptionAsync(stream, group,
                        eventAppeared: (sub, e) => EventAppeared(sub, e, clientsToken.Token, callback),
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected),
                        // Let us accept large number of unacknowledged events
                        bufferSize: _readSize * 3,
                        autoAck: false).ConfigureAwait(false);

                    lock (_subLock) _persistentSubs.Add(subscription);
                    Logger.Write(LogLevel.Info,
                        () => $"Connected to persistent subscription stream [{stream}] group [{group}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");
                }
                catch (OperationTimedOutException)
                {
                    return false;
                }
            }
            return true;
        }
        public async Task<bool> ConnectRoundRobinPersistentSubscription(string stream, string group, CancellationToken token,
            Action<string, long, IFullEvent> callback, Func<Task> disconnected)
        {
            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (var client in _clients)
            {
                Logger.Write(LogLevel.Info,
                    () => $"Connecting to persistent subscription stream [{stream}] group [{group}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");


                var settings = PersistentSubscriptionSettings.Create()
                    .StartFromBeginning()
                    .WithMaxRetriesOf(10)
                    .WithReadBatchOf(_readSize)
                    .WithLiveBufferSizeOf(_readSize * 5)
                    .WithMessageTimeoutOf(TimeSpan.FromMinutes(5))
                    .CheckPointAfter(TimeSpan.FromMinutes(1))
                    .MaximumCheckPointCountOf(_readSize * 5)
                    .ResolveLinkTos()
                    .WithNamedConsumerStrategy(SystemConsumerStrategies.RoundRobin);
                if (_extraStats)
                    settings.WithExtraStatistics();

                try
                {
                    await client.CreatePersistentSubscriptionAsync(stream, group, settings,
                        client.Settings.DefaultUserCredentials).ConfigureAwait(false);
                    Logger.Info($"Created ROUND ROBIN persistent subscription stream [{stream}] group [{group}]");
                }
                catch (InvalidOperationException)
                {
                    // Already created
                }


                try
                {
                    var subscription = await client.ConnectToPersistentSubscriptionAsync(stream, group,
                        eventAppeared: (sub, e) => EventAppeared(sub, e, clientsToken.Token, callback),
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected),
                        // Let us accept large number of unacknowledged events
                        bufferSize: _readSize * 3,
                        autoAck: false).ConfigureAwait(false);

                    lock (_subLock) _persistentSubs.Add(subscription);
                    Logger.Write(LogLevel.Info,
                        () => $"Connected to persistent subscription stream [{stream}] group [{group}] on client {client.Settings.GossipSeeds[0].EndPoint.Address}");
                }
                catch (OperationTimedOutException)
                {
                    return false;
                }
            }
            return true;
        }

        public Task Acknowledge(IFullEvent @event)
        {
            return Acknowledge(new[] {@event});
        }
        public Task Acknowledge(IEnumerable<IFullEvent> events)
        {
            var toAck = new Dictionary<EventStorePersistentSubscriptionBase, List<Guid>>();
            foreach (var @event in events)
            {
                Tuple<EventStorePersistentSubscriptionBase, Guid> outstanding;
                if (!@event.EventId.HasValue || !_outstandingEvents.TryRemove(@event.EventId.Value, out outstanding))
                {
                    Logger.Warn($"Tried to ACK unknown event {@event.EventId}");
                    continue;
                }
                
                if (!toAck.ContainsKey(outstanding.Item1))
                    toAck[outstanding.Item1] = new List<Guid>();

                toAck[outstanding.Item1].Add(outstanding.Item2);
            }

            foreach (var ack in toAck)
                ack.Key.Acknowledge(ack.Value);

            return Task.CompletedTask;
        }

        private void EventAppeared(EventStorePersistentSubscriptionBase sub, ResolvedEvent e, CancellationToken token,
            Action<string, long, IFullEvent> callback)
        {
            if (token.IsCancellationRequested)
            {
                sub.Stop(TimeSpan.FromSeconds(5));
                lock (_subLock) _persistentSubs.Remove(sub);
                token.ThrowIfCancellationRequested();
            }
            _outstandingEvents[e.Event.EventId] = new Tuple<EventStorePersistentSubscriptionBase,Guid>(sub, e.OriginalEvent.EventId);

            EventAppeared(e, token, callback);
        }

        private void EventAppeared(EventStoreCatchUpSubscription sub, ResolvedEvent e, CancellationToken token,
            Action<string, long, IFullEvent> callback)
        {
            if (token.IsCancellationRequested)
            {
                sub.Stop();
                lock (_subLock) _subscriptions.Remove(sub);
                token.ThrowIfCancellationRequested();
            }
            EventAppeared(e, token, callback);
        }

        private void EventAppeared(ResolvedEvent e, CancellationToken token, Action<string, long, IFullEvent> callback)
        {
            Logger.Write(LogLevel.Debug,
                    () => $"Event appeared {e.Event?.EventId ?? Guid.Empty} in subscription stream [{e.Event?.EventStreamId ?? ""}] number {e.Event?.EventNumber ?? -1} projection event number {e.OriginalEventNumber}");

            // Don't care about metadata streams
            if (e.Event == null || e.Event.EventStreamId[0] == '$')
                return;

            var metadata = e.Event.Metadata;
            var data = e.Event.Data;

            var descriptor = metadata.Deserialize(_settings);

            if (descriptor.Compressed)
                data = data.Decompress();

            var payload = data.Deserialize(e.Event.EventType, _settings);

            callback(e.Event.EventStreamId, e.Event.EventNumber, new WritableEvent
            {
                Descriptor = descriptor,
                Event = payload,
                EventId = e.Event.EventId
            });
        }

        private void SubscriptionDropped(EventStoreCatchUpSubscription sub, SubscriptionDropReason reason, Exception ex, Func<Task> disconnected)
        {
            Logger.Write(LogLevel.Info, () => $"Disconnected from subscription.  Reason: {reason} Exception: {ex}");

            lock (_subLock) _subscriptions.Remove(sub);
            if (reason == SubscriptionDropReason.UserInitiated) return;

            // Run via task because we are currently on the thread that would process a reconnect and we shouldn't block it
            Task.Run(disconnected);
        }
        private void SubscriptionDropped(EventStorePersistentSubscriptionBase sub, SubscriptionDropReason reason, Exception ex, Func<Task> disconnected)
        {
            Logger.Write(LogLevel.Info, () => $"Disconnected from subscription.  Reason: {reason} Exception: {ex}");

            lock (_subLock) _persistentSubs.Remove(sub);
            if (reason == SubscriptionDropReason.UserInitiated) return;

            // Run via task because we are currently on the thread that would process a reconnect and we shouldn't block it
            Task.Run(disconnected);
        }



        public async Task<bool> EnableProjection(string name)
        {

            foreach (var connection in _clients)
            {

                var manager = new ProjectionsManager(connection.Settings.Log,
                    new IPEndPoint(connection.Settings.GossipSeeds[0].EndPoint.Address,
                        connection.Settings.ExternalGossipPort), TimeSpan.FromSeconds(5));
                try
                {
                    await manager.EnableAsync(name, connection.Settings.DefaultUserCredentials).ConfigureAwait(false);
                }
                catch (OperationTimedOutException)
                {
                    return false;
                }
            }
            return true;
        }

        public async Task<bool> CreateProjection(string name, string definition)
        {
            // Normalize new lines
            definition = definition.Replace(Environment.NewLine, "\n");

            foreach (var client in _clients)
            {

                var manager = new ProjectionsManager(client.Settings.Log,
                    new IPEndPoint(client.Settings.GossipSeeds[0].EndPoint.Address,
                        client.Settings.ExternalGossipPort), TimeSpan.FromSeconds(5));

                try
                {
                    var existing = await manager.GetQueryAsync(name).ConfigureAwait(false);

                    // Remove all whitespace and new lines that could be different on different platforms and don't affect actual projection
                    var fixedExisting = Regex.Replace(existing, @"\s+", String.Empty);
                    var fixedDefinition = Regex.Replace(definition, @"\s+", String.Empty);


                    if (!string.Equals(fixedExisting, fixedDefinition, StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Fatal(
                            $"Projection [{name}] already exists and is a different version!  If you've upgraded your code don't forget to bump your app's version!\nExisting:\n{existing}\nDesired:\n{definition}");
                        throw new EndpointVersionException(
                            $"Projection [{name}] already exists and is a different version!  If you've upgraded your code don't forget to bump your app's version!");
                    }
                }
                catch (ProjectionCommandFailedException)
                {
                    try
                    {
                        // Projection doesn't exist 
                        await
                            manager.CreateContinuousAsync(name, definition, false, client.Settings.DefaultUserCredentials)
                                .ConfigureAwait(false);
                    }
                    catch (ProjectionCommandFailedException)
                    {
                    }
                }
            }
            return true;
        }
    }
}
