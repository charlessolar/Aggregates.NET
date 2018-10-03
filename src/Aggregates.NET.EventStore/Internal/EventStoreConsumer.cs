using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    [ExcludeFromCodeCoverage]
    internal class EventStoreConsumer : IEventStoreConsumer, IDisposable
    {
        private static readonly ILog Logger = LogProvider.GetLogger("EventStoreConsumer");

        private readonly IMetrics _metrics;
        private readonly IMessageSerializer _serializer;
        private readonly IVersionRegistrar _registrar;
        private readonly IEventStoreConnection[] _clients;
        private readonly IEventMapper _mapper;
        private readonly int _readSize;
        private readonly bool _extraStats;
        private readonly object _subLock;
        private readonly List<EventStoreCatchUpSubscription> _subscriptions;
        private readonly List<EventStorePersistentSubscriptionBase> _persistentSubs;
        private readonly ConcurrentDictionary<string, Tuple<EventStorePersistentSubscriptionBase, Guid>> _outstandingEvents;
        private bool _disposed;

        public EventStoreConsumer(IMetrics metrics, IMessageSerializer serializer, IVersionRegistrar registrar, IEventStoreConnection[] clients, IEventMapper mapper)
        {
            _metrics = metrics;
            _serializer = serializer;
            _clients = clients;
            _mapper = mapper;
            _registrar = registrar;

            _readSize = Configuration.Settings.ReadSize;
            _extraStats = Configuration.Settings.ExtraStats;
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



        public Task<bool> SubscribeToStreamStart(string stream, CancellationToken token, Func<string, long, IFullEvent, Task> callback, Func<Task> disconnected)
        {
            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (var client in _clients)
            {
                Logger.InfoEvent("BeginSubscribe", "[{Stream:l}] store {Store}", stream, client.Settings.GossipSeeds[0].EndPoint.Address);

                var settings = new CatchUpSubscriptionSettings(1000, 50, Logger.IsDebugEnabled(), true);
                var startingNumber = 0L;
                try
                {
                    var subscription = client.SubscribeToStreamFrom(stream,
                        startingNumber,
                        settings,
                        eventAppeared: (sub, e) => EventAppeared(sub, e, clientsToken.Token, callback),
                        subscriptionDropped: (sub, reason, ex) => SubscriptionDropped(sub, reason, ex, disconnected, clientsToken.Token));
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

        public async Task<bool> SubscribeToStreamEnd(string stream, CancellationToken token, Func<string, long, IFullEvent, Task> callback, Func<Task> disconnected)
        {
            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (var client in _clients)
            {
                try
                {
                    Logger.InfoEvent("EndSubscribe", "End of [{Stream:l}] store {Store}", stream, client.Settings.GossipSeeds[0].EndPoint.Address);
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
            Func<string, long, IFullEvent, Task> callback, Func<Task> disconnected)
        {
            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (var client in _clients)
            {
                Logger.InfoEvent("PersistentSubscribe", "Persistent [{Stream:l}] group [{Group:l}] store {Store}", stream, group, client.Settings.GossipSeeds[0].EndPoint.Address);


                var settings = PersistentSubscriptionSettings.Create()
                    .StartFromBeginning()
                    .WithMaxRetriesOf(10)
                    .WithReadBatchOf(_readSize)
                    .WithBufferSizeOf(_readSize * 3)
                    .WithLiveBufferSizeOf(_readSize)
                    //.DontTimeoutMessages()
                    .WithMessageTimeoutOf(TimeSpan.FromMinutes(2))
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
                    Logger.InfoEvent("CreatePinned", "[{Stream:l}] group [{Group:l}]", stream, group);
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
                }
                catch (OperationTimedOutException)
                {
                    return false;
                }
            }
            return true;
        }
        public async Task<bool> ConnectRoundRobinPersistentSubscription(string stream, string group, CancellationToken token,
            Func<string, long, IFullEvent, Task> callback, Func<Task> disconnected)
        {
            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(token);
            foreach (var client in _clients)
            {
                Logger.InfoEvent("PersistentSubscribe", "Persistent [{Stream:l}] group [{Group:l}] store {Store}", stream, group, client.Settings.GossipSeeds[0].EndPoint.Address);


                var settings = PersistentSubscriptionSettings.Create()
                    .StartFromBeginning()
                    .WithMaxRetriesOf(10)
                    .WithReadBatchOf(_readSize)
                    .WithBufferSizeOf(_readSize * 3)
                    .WithLiveBufferSizeOf(_readSize)
                    //.DontTimeoutMessages()
                    .WithMessageTimeoutOf(TimeSpan.FromMinutes(2))
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
                    Logger.InfoEvent("CreateRoundRobin", "[{Stream:l}] group [{Group:l}]", stream, group);
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
                Logger.WarnEvent("ACK", "Unknown ack {EventId}", @event.EventId);
                return Task.CompletedTask;
            }

            outstanding.Item1.Acknowledge(outstanding.Item2);
            return Task.CompletedTask;
        }

        private async Task EventAppeared(EventStorePersistentSubscriptionBase sub, ResolvedEvent e, CancellationToken token,
            Func<string, long, IFullEvent, Task> callback)
        {
            // Don't care about metadata streams
            if (e.Event == null || e.Event.EventStreamId[0] == '$')
            {
                sub.Acknowledge(e.OriginalEvent.EventId);
                return;
            }

            if (token.IsCancellationRequested)
            {
                Logger.WarnEvent("Cancelation", "Token cancel requested");
                ThreadPool.QueueUserWorkItem((_) => sub.Stop(TimeSpan.FromSeconds(10)));
                token.ThrowIfCancellationRequested();
            }

            var eventId = $"{e.Event.EventId}:{e.Event.EventStreamId}:{e.Event.EventNumber}";
            _outstandingEvents[eventId] = new Tuple<EventStorePersistentSubscriptionBase, Guid>(sub, e.OriginalEvent.EventId);
            
            try
            {
                await EventAppeared(e, token, callback).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.ErrorEvent("AppearedException", ex, "Stream: [{Stream:l}] Position: {StreamPosition} {ExceptionType} - {ExceptionMessage}", e.Event.EventStreamId, e.Event.EventNumber, ex.GetType().Name, ex.Message);
                sub.Fail(e, PersistentSubscriptionNakEventAction.Park, ex.GetType().Name);
                // don't throw, stops subscription and causes reconnect
                //throw;
            }
        }

        private async Task EventAppeared(EventStoreCatchUpSubscription sub, ResolvedEvent e, CancellationToken token,
            Func<string, long, IFullEvent, Task> callback)
        {
            // Don't care about metadata streams
            if (e.Event == null || e.Event.EventStreamId[0] == '$')
                return;

            if (token.IsCancellationRequested)
            {
                Logger.WarnEvent("Cancelation", "Token cancel requested");
                ThreadPool.QueueUserWorkItem((_) => sub.Stop(TimeSpan.FromSeconds(10)));
                token.ThrowIfCancellationRequested();
            }
            try
            {
                await EventAppeared(e, token, callback).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.ErrorEvent("AppearedException", ex, "Stream: [{Stream:l}] Position: {StreamPosition} {ExceptionType} - {ExceptionMessage}", e.Event.EventStreamId, e.Event.EventNumber, ex.GetType().Name, ex.Message);
                //throw;
            }
        }

        private Task EventAppeared(ResolvedEvent e, CancellationToken token, Func<string, long, IFullEvent, Task> callback)
        {
            var metadata = e.Event.Metadata;
            var data = e.Event.Data;

            var descriptor = _serializer.Deserialize<EventDescriptor>(metadata);

            if (descriptor.Compressed)
                data = data.Decompress();

            var eventType = _registrar.GetNamedType(e.Event.EventType);
            // Not all types are detected and initialized by NSB - they do it in the pipeline, we have to do it here
            _mapper.Initialize(eventType);
            
            var payload = _serializer.Deserialize(eventType, data) as IEvent;

            return callback(e.Event.EventStreamId, e.Event.EventNumber, new FullEvent
            {
                Descriptor = descriptor,
                Event = payload,
                EventId = e.Event.EventId
            });
        }

        private void SubscriptionDropped(EventStoreCatchUpSubscription sub, SubscriptionDropReason reason, Exception ex, Func<Task> disconnected, CancellationToken token)
        {
            Logger.InfoEvent("Disconnect", "{Reason}: {ExceptionType} - {ExceptionMessage}", reason, ex.GetType().Name, ex.Message);

            lock (_subLock) _subscriptions.Remove(sub);
            if (reason == SubscriptionDropReason.UserInitiated) return;
            if (token.IsCancellationRequested) return;

            // Run via task because we are currently on the thread that would process a reconnect and we shouldn't block it
            Task.Run(disconnected);
        }
        private void SubscriptionDropped(EventStorePersistentSubscriptionBase sub, SubscriptionDropReason reason, Exception ex, Func<Task> disconnected, CancellationToken token)
        {
            Logger.InfoEvent("Disconnect", "{Reason}: {ExceptionType} - {ExceptionMessage}", reason, ex.GetType().Name, ex.Message);

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
                var manager = new ProjectionsManager(connection.Settings.Log,
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
                        throw new EndpointVersionException(name, existing, definition);
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
