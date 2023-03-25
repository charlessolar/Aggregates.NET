using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Messages;
using EventStore.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class EventStoreClient : IEventStoreClient
    {

        private readonly Microsoft.Extensions.Logging.ILogger Logger;
        private readonly IMessageSerializer _serializer;
        private readonly IVersionRegistrar _registrar;
        private readonly ISettings _settings;
        private readonly IMetrics _metrics;
        private readonly IEventMapper _mapper;
        private readonly IEnumerable<Func<IMutate>> _mutators;

        private readonly ConcurrentDictionary<string, EventStore.Client.EventStoreClient> _connections;
        private readonly ConcurrentDictionary<string, EventStore.Client.EventStoreProjectionManagementClient> _projectionConnections;
        private readonly ConcurrentDictionary<string, EventStore.Client.EventStorePersistentSubscriptionsClient> _persistentSubConnections;

        private readonly object _subLock = new object();
        private readonly List<StreamSubscription> _subscriptions;
        private readonly List<PersistentSubscription> _persistentSubs;

        private readonly CancellationTokenSource _csc;
        private bool _disposed;

        public EventStoreClient(
            ILogger<EventStoreClient> logger,
            IMessageSerializer serializer,
            IVersionRegistrar registrar,
            ISettings settings,
            IMetrics metrics,
            IEventMapper mapper,
            IEnumerable<Func<IMutate>> mutators,
            IEnumerable<EventStore.Client.EventStoreClient> connections,
            IEnumerable<EventStore.Client.EventStoreProjectionManagementClient> projectionConnections,
            IEnumerable<EventStore.Client.EventStorePersistentSubscriptionsClient> persistentSubConnections
            )
        {
            Logger = logger;
            _serializer = serializer;
            _registrar = registrar;
            _settings = settings;
            _metrics = metrics;
            _mapper = mapper;
            _mutators = mutators;

            _csc = new CancellationTokenSource();

            _connections = new ConcurrentDictionary<string, EventStore.Client.EventStoreClient>();
            _projectionConnections = new ConcurrentDictionary<string, EventStore.Client.EventStoreProjectionManagementClient>();
            _persistentSubConnections = new ConcurrentDictionary<string, EventStorePersistentSubscriptionsClient>();


            _subscriptions = new List<StreamSubscription>();
            _persistentSubs = new List<PersistentSubscription>();

            foreach (var connection in connections)
            {
                Logger.InfoEvent("Initiate", "Registered EventStore client {ClientName}", connection.ConnectionName);
                _connections[connection.ConnectionName] = connection;
            }
            foreach (var connection in projectionConnections)
            {
                Logger.InfoEvent("Initiate", "Registered EventStore projection client {ClientName}", connection.ConnectionName);
                _projectionConnections[connection.ConnectionName] = connection;
            }
            foreach (var connection in persistentSubConnections)
            {
                Logger.InfoEvent("Initiate", "Registered EventStore persistent subscription client {ClientName}", connection.ConnectionName);
                _persistentSubConnections[connection.ConnectionName] = connection;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var con in _persistentSubConnections.Values)
                con.Dispose();
            foreach (var con in _projectionConnections.Values)
                con.Dispose();
            foreach (var con in _connections.Values)
                con.Dispose();
        }


        public async Task<bool> SubscribeToStreamStart(string stream, IEventStoreClient.EventAppeared callback)
        {

            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(_csc.Token);
            foreach (var connection in _connections)
            {
                Logger.InfoEvent("Start Subscribe", "Subscribe to beginning of [{Stream:l}] store [{Store}]", stream, connection.Value.ConnectionName);


                try
                {
                    var subscription = await connection.Value.SubscribeToStreamAsync(
                        stream,
                        FromStream.Start,
                        cancellationToken: clientsToken.Token,
                        eventAppeared: (sub, e, token) => eventAppeared(sub, e, token, callback),
                        subscriptionDropped: (sub, reason, ex) =>
                            subscriptionDropped(sub, reason, ex, clientsToken.Token,
                            () => SubscribeToStreamEnd(stream, callback)
                        ));
                    lock (_subLock) _subscriptions.Add(subscription);

                }
                catch
                {
                    // If one fails, cancel all the others
                    clientsToken.Cancel();
                }
            }
            return !clientsToken.IsCancellationRequested;
        }

        public async Task<bool> SubscribeToStreamEnd(string stream, IEventStoreClient.EventAppeared callback)
        {

            var clientsToken = CancellationTokenSource.CreateLinkedTokenSource(_csc.Token);
            foreach (var connection in _connections)
            {
                try
                {
                    Logger.InfoEvent("EndSubscribe", "Subscribe to end of [{Stream:l}] store [{Store}]", stream, connection.Value.ConnectionName);

                    var sub = await connection.Value.SubscribeToStreamAsync(
                        stream,
                        FromStream.End,
                        cancellationToken: clientsToken.Token,
                        eventAppeared: (sub, e, token) => eventAppeared(sub, e, token, callback),
                        subscriptionDropped: (sub, reason, ex) =>
                            subscriptionDropped(sub, reason, ex, clientsToken.Token,
                            () => SubscribeToStreamEnd(stream, callback)
                        ));

                    lock (_subLock) _subscriptions.Add(sub);
                }
                catch
                {
                    // If one fails, cancel all the others
                    clientsToken.Cancel();
                }
            }
            return !clientsToken.IsCancellationRequested;
        }

        public async Task<bool> ConnectPinnedPersistentSubscription(string stream, string group, IEventStoreClient.EventAppeared callback)
        {
            var start = _settings.DevelopmentMode ? EventStore.Client.StreamPosition.End : EventStore.Client.StreamPosition.Start;

            var settings = new PersistentSubscriptionSettings(
                resolveLinkTos: true,
                startFrom: start,
                extraStatistics: _settings.ExtraStats,
                maxRetryCount: _settings.Retries,
                consumerStrategyName: SystemConsumerStrategies.Pinned
                );


            foreach (var connection in _persistentSubConnections)
            {
                var store = connection.Value;
                try
                {

                    await store.CreateAsync(stream, group, settings).ConfigureAwait(false);
                    Logger.InfoEvent("CreatePinned", "Creating pinned subscription to [{Stream:l}] group [{Group:l}]", stream, group);
                }
                catch
                {
                    // Already created
                }

                var subCancel = CancellationTokenSource.CreateLinkedTokenSource(_csc.Token);

                try
                {
                    var subscription = await store.SubscribeToStreamAsync(stream, group,
                        eventAppeared: (sub, e, retry, token) => eventAppeared(sub, e, token, callback),
                        // auto reconnect to subscription
                        subscriptionDropped: (sub, reason, ex) =>
                            subscriptionDropped(sub, reason, ex, subCancel.Token,
                            () => ConnectPinnedPersistentSubscription(stream, group, callback)
                        ),
                        cancellationToken: subCancel.Token
                        ).ConfigureAwait(false);

                    lock (_subLock) _persistentSubs.Add(subscription);
                }
                catch
                {
                    // if one fails they all fail
                    subCancel.Cancel();
                    return false;
                }
            }
            return true;
        }
        public Task<T> GetProjectionResult<T>(string name, string partition)
        {
            var shard = Math.Abs(partition.GetHash() % _projectionConnections.Count());
            var connection = _projectionConnections.ElementAt(shard);

            try
            {
                Logger.DebugEvent("EventStore", "Getting projection result from [{Name}] partition [{Partition}]", name, partition);
                return connection.Value.GetResultAsync<T>(name, partition);
            }
            catch
            {
                return Task.FromResult(default(T));
            }
        }

        public async Task<bool> EnableProjection(string name)
        {

            foreach (var connection in _projectionConnections)
            {

                try
                {
                    await connection.Value.EnableAsync(name).ConfigureAwait(false);
                }
                catch
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

            foreach (var connection in _projectionConnections)
            {
                var count = 0;
                // if 2 projections from different endpoints are created at roughly the same time
                // ES will throw an error to us. 
                // but that error is indistinguishable from "already exists" error
                // so in case of any error retry 3 times
                while (count < 3)
                {
                    try {
                        // todo: the client will eventually have "emit" option
                        // https://github.com/EventStore/EventStore/pull/3384
                        await connection.Value.CreateContinuousAsync(name, definition, trackEmittedStreams: true).ConfigureAwait(false);
                        break;
                    } catch (RpcException e) when (e.StatusCode is StatusCode.Unknown) {
						Logger.WarnEvent("Projection", "Unknown error when creating projection [{Name}]: {Message}", name, e.Message);
						try {
							// If in development mode try to update it incase theres new events to consider
							if (_settings.DevelopmentMode) {
								await connection.Value.UpdateAsync(name, definition).ConfigureAwait(false);
							}
						} catch {
						}
					} catch (RpcException e) when (e.StatusCode is StatusCode.AlreadyExists) {
                        Logger.WarnEvent("Projection", "Projection [{Name}] already exists", name);
                        try {
                            // If in development mode try to update it incase theres new events to consider
                            if (_settings.DevelopmentMode) {
                                await connection.Value.UpdateAsync(name, definition).ConfigureAwait(false);
                            }
                        } catch {
                        }
                    } catch (Exception ex) {
                        Logger.ErrorEvent("Projection", ex, "Failed to create projection [{Name}]", name);
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                    count++;
                }
            }
            return true;
        }


        private async Task eventAppeared(StreamSubscription sub, ResolvedEvent e, CancellationToken token, IEventStoreClient.EventAppeared callback)
        {
            // Don't care about metadata streams
            if (e.Event == null || e.Event.EventStreamId[0] == '$')
                return;

            if (token.IsCancellationRequested)
            {
                Logger.WarnEvent("Cancelation", "Event [{EventId:l}] from  stream [{Stream}] appeared while closing catchup subscription", e.OriginalEvent.EventId, e.OriginalStreamId);
                token.ThrowIfCancellationRequested();
            }
            try
            {
                await deserializeEvent(e, callback).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.ErrorEvent("Event Failed", ex, "Event processing failed - Stream: [{Stream:l}] Position: {StreamPosition} {ExceptionType} - {ExceptionMessage}", e.Event.EventStreamId, e.Event.EventNumber, ex.GetType().Name, ex.Message);
                //throw;
            }
        }
        private async Task eventAppeared(PersistentSubscription sub, ResolvedEvent e, CancellationToken token, IEventStoreClient.EventAppeared callback)
        {
            // Don't care about metadata streams
            if (e.Event == null || e.Event.EventStreamId[0] == '$')
            {
                await sub.Ack(e).ConfigureAwait(false);
                return;
            }

            if (token.IsCancellationRequested)
            {
                Logger.WarnEvent("Cancelation", "Event [{EventId:l}] from stream [{Stream}] appeared while closing subscription", e.OriginalEvent.EventId, e.OriginalStreamId);
                await sub.Nack(PersistentSubscriptionNakEventAction.Retry, "Shutting down", e).ConfigureAwait(false);
                return;
            }

            try
            {
                await deserializeEvent(e, callback).ConfigureAwait(false);
                await sub.Ack(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.ErrorEvent("Event Failed", ex, "Event processing failed - Stream: [{Stream:l}] Position: {StreamPosition} {ExceptionType} - {ExceptionMessage}", e.Event.EventStreamId, e.Event.EventNumber, ex.GetType().Name, ex.Message);
                await sub.Nack(PersistentSubscriptionNakEventAction.Park, $"Deserialize exception {ex.GetType().Name} - {ex.Message}", e).ConfigureAwait(false);
                // don't throw, stops subscription and causes reconnect
            }
        }

        private Task deserializeEvent(ResolvedEvent e, IEventStoreClient.EventAppeared callback)
        {
            var metadata = e.Event.Metadata;
            var data = e.Event.Data;

            IEventDescriptor descriptor;

            try
            {
                descriptor = _serializer.Deserialize<EventDescriptor>(metadata.ToArray());
            }
            catch (SerializationException)
            {
                // Try the old format
                descriptor = _serializer.Deserialize<LegacyEventDescriptor>(metadata.ToArray());
            }

            if (descriptor.Compressed)
                data = data.ToArray().Decompress();

            var eventType = _registrar.GetNamedType(e.Event.EventType);
            _mapper.Initialize(eventType);

            var payload = _serializer.Deserialize(eventType, data.ToArray()) as IEvent;

            return callback(e.Event.EventStreamId, e.Event.EventNumber.ToInt64(), new FullEvent
            {
                Descriptor = descriptor,
                Event = payload,
                EventId = e.Event.EventId.ToGuid()
            });
        }

        private void subscriptionDropped(StreamSubscription sub, SubscriptionDroppedReason reason, Exception ex, CancellationToken token, Func<Task> disconnected)
        {
            Logger.InfoEvent("Disconnect", "Subscription has been dropped because {Reason}: {ExceptionType} - {ExceptionMessage}", reason, ex.GetType().Name, ex.Message);

            lock (_subLock) _subscriptions.Remove(sub);
            if (reason == SubscriptionDroppedReason.Disposed) return;
            if (token.IsCancellationRequested) return;

            // Run via task because we are currently on the thread that would process a reconnect and we shouldn't block it
            Task.Run(disconnected);
        }
        private void subscriptionDropped(PersistentSubscription sub, SubscriptionDroppedReason reason, Exception ex, CancellationToken token, Func<Task> disconnected)
        {
            Logger.InfoEvent("Disconnect", "Persistent subscription has been dropped because {Reason}: {ExceptionType} - {ExceptionMessage}", reason, ex.GetType().Name, ex.Message);

            lock (_subLock) _persistentSubs.Remove(sub);
            if (reason == SubscriptionDroppedReason.Disposed) return;
            if (token.IsCancellationRequested) return;

            // Run via task because we are currently on the thread that would process a reconnect and we shouldn't block it
            Task.Run(disconnected);
        }




        public async Task<IFullEvent[]> GetEvents(StreamDirection direction, string stream, long? start = null, int? count = null)
        {
            var shard = Math.Abs(stream.GetHash() % _connections.Count());

            var sliceStart = start.HasValue ? EventStore.Client.StreamPosition.FromInt64(start.Value) : (direction == StreamDirection.Forwards ? EventStore.Client.StreamPosition.Start : EventStore.Client.StreamPosition.End);

            ResolvedEvent[] events;
            var client = _connections.ElementAt(shard);
            using (var ctx = _metrics.Begin("EventStore Read Time"))
            {
                try
                {
                    var eventReader = client.Value.ReadStreamAsync((Direction)direction, stream, sliceStart, cancellationToken: _csc.Token);
                    if (await eventReader.ReadState == ReadState.StreamNotFound)
                        throw new NotFoundException(stream, client.Value.ConnectionName);

                    if (count.HasValue)
                        events = await eventReader.Take(count.Value).ToArrayAsync(_csc.Token).ConfigureAwait(false);
                    else
                        events = await eventReader.ToArrayAsync(_csc.Token).ConfigureAwait(false);
                }
                catch
                {
                    throw new NotFoundException(stream, client.Value.ConnectionName);
                }

                if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                    Logger.InfoEvent("Slow Alarm", "Reading {Events} events size {Size} stream [{Stream:l}] elapsed {Milliseconds} ms", events.Length, events.Sum(x => x.Event.Data.Length), stream, ctx.Elapsed.TotalMilliseconds);
                Logger.DebugEvent("Read", "{Events} events size {Size} stream [{Stream:l}] elapsed {Milliseconds} ms", events.Length, events.Sum(x => x.Event.Data.Length), stream, ctx.Elapsed.TotalMilliseconds);
            }

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;

                IEventDescriptor descriptor;

                try
                {
                    descriptor = _serializer.Deserialize<EventDescriptor>(metadata.ToArray());
                }
                catch (SerializationException)
                {
                    // Try the old format
                    descriptor = _serializer.Deserialize<LegacyEventDescriptor>(metadata.ToArray());
                }

                if (descriptor == null || descriptor.EventId == Guid.Empty)
                {
                    // Assume we read from a children projection
                    // (theres no way to set metadata in projection states)

                    var children = _serializer.Deserialize<ChildrenProjection>(data.ToArray());
                    if (!(children is IEvent))
                        throw new UnknownMessageException(e.Event.EventType);

                    return new FullEvent
                    {
                        Descriptor = null,
                        Event = children as IEvent,
                        EventId = Guid.Empty
                    };
                }
                if (descriptor.Compressed)
                    data = data.ToArray().Decompress();

                var eventType = _registrar.GetNamedType(e.Event.EventType);

                var @event = _serializer.Deserialize(eventType, data.ToArray());

                if (!(@event is IEvent))
                    throw new UnknownMessageException(e.Event.EventType);

                // Special case if event was written without a version - substitue the position from store
                //if (descriptor.Version == 0)
                //    descriptor.Version = e.Event.EventNumber;

                return (IFullEvent)new FullEvent
                {
                    Descriptor = descriptor,
                    Event = @event as IEvent,
                    EventId = e.Event.EventId.ToGuid()
                };
            }).ToArray();

            return translatedEvents;
        }


        public Task<long> WriteEvents(string stream, IFullEvent[] events,
            IDictionary<string, string> commitHeaders, long? expectedVersion = null)
        {

            var translatedEvents = events.Select(e =>
            {
                IMutating mutated = new Mutating(e.Event, e.Descriptor.Headers ?? new Dictionary<string, string>());

                foreach (var mutator in _mutators)
                    mutated = mutator().MutateOutgoing(mutated);

                var mappedType = e.Event.GetType();
                if (!mappedType.IsInterface)
                    mappedType = _mapper.GetMappedTypeFor(mappedType) ?? mappedType;

                var descriptor = new EventDescriptor
                {
                    EventId = e.EventId ?? Guid.NewGuid(),
                    CommitHeaders = (commitHeaders ?? new Dictionary<string, string>()).Merge(new Dictionary<string, string>
                    {
                        [Defaults.InstanceHeader] = Defaults.Instance.ToString(),
                        [Defaults.EndpointHeader] = _settings.Endpoint,
                        [Defaults.EndpointVersionHeader] = _settings.EndpointVersion.ToString(),
                        [Defaults.AggregatesVersionHeader] = _settings.AggregatesVersion.ToString(),
                        [Defaults.MachineHeader] = Environment.MachineName,
                    }),
                    Compressed = _settings.Compression.HasFlag(Compression.Events),
                    EntityType = e.Descriptor.EntityType,
                    StreamType = e.Descriptor.StreamType,
                    Bucket = e.Descriptor.Bucket,
                    StreamId = e.Descriptor.StreamId,
                    Parents = e.Descriptor.Parents,
                    Version = e.Descriptor.Version,
                    Timestamp = e.Descriptor.Timestamp,
                    Headers = e.Descriptor.Headers,
                };

                var eventType = _registrar.GetVersionedName(mappedType);
                foreach (var header in mutated.Headers)
                    e.Descriptor.Headers[header.Key] = header.Value;


                var @event = _serializer.Serialize(mutated.Message);

                if (_settings.Compression.HasFlag(Compression.Events))
                {
                    descriptor.Compressed = true;
                    @event = @event.Compress();
                }
                var metadata = _serializer.Serialize(descriptor);

                return new EventData(
                    Uuid.FromGuid(descriptor.EventId),
                    eventType,
                    @event,
                    metadata,
                    contentType: descriptor.Compressed ? "application/x-gzip-compressed" : "application/json"
                );
            }).ToArray();

            return DoWrite(stream, translatedEvents, expectedVersion);
        }

        public async Task WriteMetadata(string stream,
            int? maxCount = null,
            long? truncateBefore = null,
            TimeSpan? maxAge = null,
            TimeSpan? cacheControl = null)
        {

            var shard = Math.Abs(stream.GetHash() % _connections.Count());
            var client = _connections.ElementAt(shard);

            Logger.DebugEvent("Metadata", "Metadata to stream [{Stream:l}] [ MaxCount: {MaxCount}, MaxAge: {MaxAge}, CacheControl: {CacheControl} ]", stream, maxCount, maxAge, cacheControl);

            try
            {
                try
                {
                    var existing = await client.Value.GetStreamMetadataAsync(stream).ConfigureAwait(false);

                    var metadata = new StreamMetadata(
                        maxCount ?? existing.Metadata.MaxCount,
                        maxAge ?? existing.Metadata.MaxAge,
                        truncateBefore.HasValue ? EventStore.Client.StreamPosition.FromInt64(truncateBefore.Value) : existing.Metadata.TruncateBefore,
                        cacheControl ?? existing.Metadata.CacheControl,
                        customMetadata: existing.Metadata.CustomMetadata
                        );

                    await client.Value.SetStreamMetadataAsync(stream, StreamRevision.FromStreamPosition(existing.MetastreamRevision.Value), metadata).ConfigureAwait(false);

                }
                catch (WrongExpectedVersionException e)
                {
                    throw new VersionException(e.Message, e);
                }
                catch (Exception e)
                {
                    throw new PersistenceException(e.Message, e);
                }
            }
            catch (Exception ex)
            {
                Logger.WarnEvent("MetadataFailure", ex, "{ExceptionType} - {ExceptionMessage}", ex.GetType().Name, ex.Message);
                throw;
            }
        }

        public async Task<bool> VerifyVersion(string stream, long expectedVersion)
        {
            var lastEvent = await GetEvents(StreamDirection.Backwards, stream, count: 1).ConfigureAwait(false);
            return (lastEvent.FirstOrDefault()?.Descriptor?.Version ?? 0L) == expectedVersion;
        }


        private async Task<long> DoWrite(string stream, EventData[] events, long? expectedVersion = null)
        {
            var shard = Math.Abs(stream.GetHash() % _connections.Count());
            var client = _connections.ElementAt(shard);

            long nextVersion;
            using (var ctx = _metrics.Begin("EventStore Write Time"))
            {
                try
                {
                    IWriteResult result;
                    if (expectedVersion.HasValue)
                        result = await
                            client.Value.AppendToStreamAsync(stream, StreamRevision.FromInt64(expectedVersion.Value), events)
                                .ConfigureAwait(false);
                    else
                        result = await
                            client.Value.AppendToStreamAsync(stream, StreamState.Any, events)
                                .ConfigureAwait(false);

                    nextVersion = result.NextExpectedStreamRevision.ToInt64();
                }
                catch (WrongExpectedVersionException e)
                {
                    throw new VersionException(e.Message, e);
                }
                catch (Exception e)
                {
                    throw new PersistenceException(e.Message, e);
                }

                if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                    Logger.InfoEvent("Slow Alarm", "Writting {Events} events size {Size} stream [{Stream:l}] version {ExpectedVersion} took {Milliseconds} ms", events.Count(), events.Sum(x => x.Data.Length), stream, expectedVersion, ctx.Elapsed.TotalMilliseconds);
                Logger.DebugEvent("Write", "{Events} events size {Size} stream [{Stream:l}] version {ExpectedVersion} took {Milliseconds} ms", events.Count(), events.Sum(x => x.Data.Length), stream, expectedVersion, ctx.Elapsed.TotalMilliseconds);

            }
            return nextVersion;
        }

    }
}
