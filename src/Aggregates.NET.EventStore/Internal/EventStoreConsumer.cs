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
using Aggregates.Logging;
using Aggregates.Messages;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Common;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.Projections;
using Newtonsoft.Json;

namespace Aggregates.Internal
{
    internal class EventStoreConsumer : IEventStoreConsumer, IDisposable
    {
        private static readonly ILog Logger = LogProvider.GetLogger("EventStoreConsumer");

        private readonly IMetrics _metrics;
        private readonly IMessageSerializer _serializer;
        private readonly IEventStoreConnection[] _clients;
        private readonly int _readSize;
        private readonly bool _extraStats;
        private readonly object _subLock;
        private readonly List<EventStoreCatchUpSubscription> _subscriptions;
        private readonly List<EventStorePersistentSubscriptionBase> _persistentSubs;
        private readonly ConcurrentDictionary<string, Tuple<EventStorePersistentSubscriptionBase, Guid>> _outstandingEvents;
        private bool _disposed;

        public EventStoreConsumer(IMetrics metrics, IMessageSerializer serializer, IEventStoreConnection[] clients, int readSize, bool extraStats)
        {
            _metrics = metrics;
            _serializer = serializer;
            _clients = clients;

            _readSize = readSize;
            _extraStats = extraStats;
            _subLock = new object();
            _subscriptions = new List<EventStoreCatchUpSubscription>();
            _persistentSubs = new List<EventStorePersistentSubscriptionBase>();
            _outstandingEvents = new ConcurrentDictionary<string, Tuple<EventStorePersistentSubscriptionBase, Guid>>();
            
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


                var settings = new CatchUpSubscriptionSettings(1000, 50, Logger.IsDebugEnabled(), true);
                var startingNumber = 0L;
                try
                {
                    var subscription = client.SubscribeToStreamFrom(stream,
                        startingNumber,
                        settings,
                        eventAppeared: (sub, e) => EventAppeared(sub, e, clientsToken.Token, callback),
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected, clientsToken.Token));
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

                    var settings = new CatchUpSubscriptionSettings(1000, 50, Logger.IsDebugEnabled(), true);

                    var startingNumber = 0L;
                    if (lastEvent.Status == SliceReadStatus.Success)
                        startingNumber = lastEvent.Events[0].OriginalEventNumber;

                    var subscription = client.SubscribeToStreamFrom(stream,
                        startingNumber,
                        settings,
                        eventAppeared: (sub, e) => EventAppeared(sub, e, clientsToken.Token, callback),
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected, clientsToken.Token));
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
                    .WithBufferSizeOf(_readSize * 3)
                    .WithLiveBufferSizeOf(_readSize)
                    .DontTimeoutMessages()
                    //.WithMessageTimeoutOf(TimeSpan.FromMinutes(2))
                    .CheckPointAfter(TimeSpan.FromSeconds(30))
                    .MaximumCheckPointCountOf(_readSize * 3)
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
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected, clientsToken.Token),
                        // Let us accept large number of unacknowledged events
                        bufferSize: _readSize,
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
                    .WithBufferSizeOf(_readSize * 3)
                    .WithLiveBufferSizeOf(_readSize)
                    .DontTimeoutMessages()
                    //.WithMessageTimeoutOf(TimeSpan.FromMinutes(2))
                    .CheckPointAfter(TimeSpan.FromSeconds(30))
                    .MaximumCheckPointCountOf(_readSize * 3)
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
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected, clientsToken.Token),
                        // Let us accept large number of unacknowledged events
                        bufferSize: _readSize,
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

        public Task Acknowledge(string stream, long position, IFullEvent @event)
        {
            var eventId = $"{@event.EventId.Value}:{stream}:{position}";
            Tuple<EventStorePersistentSubscriptionBase, Guid> outstanding;
            if (!@event.EventId.HasValue || !_outstandingEvents.TryRemove(eventId, out outstanding))
            {
                Logger.Warn($"Tried to ACK unknown event {@event.EventId}");
                return Task.CompletedTask;
            }
            _metrics.Decrement("Outstanding Events", Unit.Event);

            outstanding.Item1.Acknowledge(outstanding.Item2);
            return Task.CompletedTask;
        }

        private void EventAppeared(EventStorePersistentSubscriptionBase sub, ResolvedEvent e, CancellationToken token,
            Action<string, long, IFullEvent> callback)
        {
            // Don't care about metadata streams
            if (e.Event == null || e.Event.EventStreamId[0] == '$')
            {
                sub.Acknowledge(e.OriginalEvent.EventId);
                return;
            }

            if (token.IsCancellationRequested)
            {
                Logger.Warn($"Token cancelation requested, stopping persistent subscription");
                Task.Run(() => sub.Stop(TimeSpan.FromSeconds(5)));
                token.ThrowIfCancellationRequested();
            }

            var eventId = $"{e.Event.EventId}:{e.Event.EventStreamId}:{e.Event.EventNumber}";
            _outstandingEvents[eventId] = new Tuple<EventStorePersistentSubscriptionBase, Guid>(sub, e.OriginalEvent.EventId);
            EventAppeared(e, token, callback);
        }

        private void EventAppeared(EventStoreCatchUpSubscription sub, ResolvedEvent e, CancellationToken token,
            Action<string, long, IFullEvent> callback)
        {
            // Don't care about metadata streams
            if (e.Event == null || e.Event.EventStreamId[0] == '$')
                return;

            if (token.IsCancellationRequested)
            {
                Logger.Warn($"Token cancelation requested, stopping catchup subscription");
                Task.Run(() => sub.Stop(TimeSpan.FromSeconds(5)));
                token.ThrowIfCancellationRequested();
            }
            EventAppeared(e, token, callback);
        }

        private void EventAppeared(ResolvedEvent e, CancellationToken token, Action<string, long, IFullEvent> callback)
        {
            Logger.Write(LogLevel.Debug,
                    () => $"Event appeared {e.Event?.EventId ?? Guid.Empty} in subscription stream [{e.Event?.EventStreamId ?? ""}] number {e.Event?.EventNumber ?? -1} projection {e.OriginalStreamId} event number {e.OriginalEventNumber}");

            var metadata = e.Event.Metadata;
            var data = e.Event.Data;

            var descriptor = _serializer.Deserialize<EventDescriptor>(metadata);

            if (descriptor.Compressed)
                data = data.Decompress();
            
            var payload = _serializer.Deserialize(e.Event.EventType, data);

            _metrics.Increment("Outstanding Events", Unit.Event);

            callback(e.Event.EventStreamId, e.Event.EventNumber, new FullEvent
            {
                Descriptor = descriptor,
                Event = payload as IEvent,
                EventId = e.Event.EventId
            });
        }

        private void SubscriptionDropped(EventStoreCatchUpSubscription sub, SubscriptionDropReason reason, Exception ex, Func<Task> disconnected, CancellationToken token)
        {
            Logger.Write(LogLevel.Info, () => $"Disconnected from subscription.  Reason: {reason} Exception: {ex}");

            lock (_subLock) _subscriptions.Remove(sub);
            if (reason == SubscriptionDropReason.UserInitiated) return;
            if (token.IsCancellationRequested) return;

            // Run via task because we are currently on the thread that would process a reconnect and we shouldn't block it
            Task.Run(disconnected);
        }
        private void SubscriptionDropped(EventStorePersistentSubscriptionBase sub, SubscriptionDropReason reason, Exception ex, Func<Task> disconnected, CancellationToken token)
        {
            Logger.Write(LogLevel.Info, () => $"Disconnected from subscription.  Reason: {reason} Exception: {ex}");
            
            lock (_subLock) _persistentSubs.Remove(sub);
            if (reason == SubscriptionDropReason.UserInitiated) return;
            if (token.IsCancellationRequested) return;

            // Run via task because we are currently on the thread that would process a reconnect and we shouldn't block it
            Task.Run(disconnected);
        }



        public async Task<bool> EnableProjection(string name)
        {

            foreach (var connection in _clients)
            {
                var manager = new ProjectionsManager(connection.Settings.Log, (EndPoint)
                    new IPEndPoint(connection.Settings.GossipSeeds[0].EndPoint.Address,
                        connection.Settings.ExternalGossipPort), TimeSpan.FromSeconds(30));
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

                var manager = new ProjectionsManager(client.Settings.Log, (EndPoint)
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
                        await manager.CreateContinuousAsync(name, definition, false, client.Settings.DefaultUserCredentials)
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
