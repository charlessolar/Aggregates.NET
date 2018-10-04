using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;
using Aggregates.Messages;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Newtonsoft.Json;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class StoreEvents : IStoreEvents
    {
        private static readonly ILog Logger = LogProvider.GetLogger("StoreEvents");
        private static readonly ILog SlowLogger = LogProvider.GetLogger("Slow Alarm");
        private readonly IMetrics _metrics;
        private readonly IMessageSerializer _serializer;
        private readonly IEventMapper _mapper;
        private readonly IVersionRegistrar _registrar;
        private readonly StreamIdGenerator _generator;
        private readonly IEventStoreConnection[] _clients;
        private readonly int _readsize;
        private readonly Compression _compress;

        public StoreEvents(IMetrics metrics, IMessageSerializer serializer, IEventMapper mapper, IVersionRegistrar registrar, IEventStoreConnection[] connections)
        {
            _metrics = metrics;
            _serializer = serializer;
            _mapper = mapper;
            _registrar = registrar;
            _clients = connections;

            _generator = Configuration.Settings.Generator;
            _readsize = Configuration.Settings.ReadSize;
            _compress = Configuration.Settings.Compression;
        }

        public Task<IFullEvent[]> GetEvents<TEntity>(string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return GetEvents(stream, start, count);
        }

        public async Task<IFullEvent[]> GetEvents(string stream, long? start = null, int? count = null)
        {
            var shard = Math.Abs(stream.GetHash() % _clients.Count());

            var sliceStart = start ?? StreamPosition.Start;
            StreamEventsSlice current;

            var events = new List<ResolvedEvent>();
            using (var ctx = _metrics.Begin("EventStore Read Time"))
            {
                do
                {
                    var readsize = _readsize;
                    if (count.HasValue)
                        readsize = Math.Min(count.Value - events.Count, _readsize);

                    current =
                        await _clients[shard].ReadStreamEventsForwardAsync(stream, sliceStart, readsize, false)
                            .ConfigureAwait(false);
                    
                    events.AddRange(current.Events);
                    sliceStart = current.NextEventNumber;
                } while (!current.IsEndOfStream && (!count.HasValue || (events.Count != count.Value)));

                if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                    SlowLogger.InfoEvent("SlowRead", "{Events} events size {Size} stream [{Stream:l}] elapsed {Milliseconds} ms", events.Count, events.Sum(x => x.Event.Data.Length), stream, ctx.Elapsed.TotalMilliseconds);
                Logger.DebugEvent("Read", "{Events} events size {Size} stream [{Stream:l}] elapsed {Milliseconds} ms", events.Count, events.Sum(x => x.Event.Data.Length), stream, ctx.Elapsed.TotalMilliseconds);
            }

            if (current.Status == SliceReadStatus.StreamNotFound)
                throw new NotFoundException(stream, _clients[shard].Settings.GossipSeeds[0].EndPoint.Address);
            

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;

                var descriptor = _serializer.Deserialize<EventDescriptor>(metadata);

                if (descriptor.Compressed)
                    data = data.Decompress();

                var eventType = _registrar.GetNamedType(e.Event.EventType);
                _mapper.Initialize(eventType);

                var @event = _serializer.Deserialize(eventType, data);

                if (!(@event is IEvent))
                    throw new UnknownMessageException(@event.GetType());

                // Special case if event was written without a version - substitue the position from store
                if (descriptor.Version == 0)
                    descriptor.Version = e.Event.EventNumber;

                return (IFullEvent)new FullEvent
                {
                    Descriptor = descriptor,
                    Event = @event as IEvent,
                    EventId = e.Event.EventId
                };
            }).ToArray();

            return translatedEvents;
        }

        public async Task<IFullEvent[]> GetEventsBackwards(string stream, long? start = null, int? count = null)
        {
            var shard = Math.Abs(stream.GetHash() % _clients.Count());

            var events = new List<ResolvedEvent>();
            var sliceStart = StreamPosition.End;

            StreamEventsSlice current;
            if (start.HasValue || count == 1)
            {
                // Interesting, ReadStreamEventsBackwardAsync's [start] parameter marks start from begining of stream, not an offset from the end.
                // Read 1 event from the end, to figure out where start should be
                var result = await _clients[shard].ReadStreamEventsBackwardAsync(stream, StreamPosition.End, 1, false).ConfigureAwait(false);
                sliceStart = result.NextEventNumber - start ?? 0;

                // Special case if only 1 event is requested no reason to read any more
                if (count == 1)
                    events.AddRange(result.Events);
            }

            if (!count.HasValue || count > 1)
            {
                using (var ctx = _metrics.Begin("EventStore Read Time"))
                {
                    do
                    {
                        var take = Math.Min((count ?? int.MaxValue) - events.Count, _readsize);

                        current =
                            await _clients[shard].ReadStreamEventsBackwardAsync(stream, sliceStart, take, false)
                                .ConfigureAwait(false);
                        
                        events.AddRange(current.Events);

                        sliceStart = current.NextEventNumber;
                    } while (!current.IsEndOfStream);


                    if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                        SlowLogger.InfoEvent("SlowBackwardsRead", "{Events} events size {Size} stream [{Stream:l}] elapsed {Milliseconds} ms", events.Count, events.Sum(x => x.Event.Data.Length), stream, ctx.Elapsed.TotalMilliseconds);
                    Logger.DebugEvent("BackwardsRead", "{Events} events size {Size} stream [{Stream:l}] elapsed {Milliseconds} ms", events.Count, events.Sum(x => x.Event.Data.Length), stream, ctx.Elapsed.TotalMilliseconds);
                }

                if (current.Status == SliceReadStatus.StreamNotFound)
                    throw new NotFoundException(stream, _clients[shard].Settings.GossipSeeds[0].EndPoint.Address);

            }

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;


                var descriptor = _serializer.Deserialize<EventDescriptor>(metadata);

                if (descriptor.Compressed)
                    data = data.Decompress();

                var eventType = _registrar.GetNamedType(e.Event.EventType);
                _mapper.Initialize(eventType);

                var @event = _serializer.Deserialize(eventType, data);

                if (!(@event is IEvent))
                    throw new UnknownMessageException(@event.GetType());
                // Special case if event was written without a version - substitute the position from store
                if (descriptor.Version == 0)
                    descriptor.Version = e.Event.EventNumber;

                return (IFullEvent)new FullEvent
                {
                    Descriptor = descriptor,
                    Event = @event as IEvent,
                    EventId = e.Event.EventId
                };
            }).ToArray();

            return translatedEvents;
        }
        public Task<IFullEvent[]> GetEventsBackwards<TEntity>(string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return GetEventsBackwards(stream, start, count);
        }

        public async Task<bool> VerifyVersion(string stream, long expectedVersion)
        {
            var size = await Size(stream).ConfigureAwait(false);
            return size == expectedVersion;
        }
        public async Task<bool> VerifyVersion<TEntity>(string bucket, Id streamId, Id[] parents,
            long expectedVersion) where TEntity : IEntity
        {
            var size = await Size<TEntity>(bucket, streamId, parents).ConfigureAwait(false);
            return size == expectedVersion;
        }


        public async Task<long> Size(string stream)
        {
            var shard = Math.Abs(stream.GetHash() % _clients.Count());

            var result = await _clients[shard].ReadStreamEventsBackwardAsync(stream, StreamPosition.End, 1, false).ConfigureAwait(false);
            var size = result.Status == SliceReadStatus.Success ? result.NextEventNumber : 0;
            Logger.DebugEvent("Size", "[{Stream:l}] size {Size}", stream, size);
            return size;

        }
        public Task<long> Size<TEntity>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return Size(stream);
        }

        public Task<long> WriteEvents(string stream, IFullEvent[] events,
            IDictionary<string, string> commitHeaders, long? expectedVersion = null)
        {
            var mutators = MutationManager.Registered.ToList();

            var translatedEvents = events.Select(e =>
            {
                IMutating mutated = new Mutating(e.Event, e.Descriptor.Headers ?? new Dictionary<string, string>());

                // use async local container first if one exists
                // (is set by unit of work - it creates a child container which mutators might need)
                IContainer container = Configuration.Settings.LocalContainer.Value;
                if (container == null)
                    container = Configuration.Settings.Container;

                foreach (var type in mutators)
                {
                    var mutator = (IMutate)container.TryResolve(type);
                    if (mutator == null)
                    {
                        Logger.WarnEvent("MutateFailure", "Failed to construct mutator {Mutator}", type.FullName);
                        continue;
                    }

                    mutated = mutator.MutateOutgoing(mutated);
                }
                var mappedType = e.Event.GetType();
                if (!mappedType.IsInterface)
                    mappedType = _mapper.GetMappedTypeFor(mappedType) ?? mappedType;

                var descriptor = new EventDescriptor
                {
                    EventId = e.EventId ?? Guid.NewGuid(),
                    CommitHeaders = (commitHeaders ?? new Dictionary<string, string>()).Merge(new Dictionary<string, string>
                    {
                        [Defaults.InstanceHeader] = Defaults.Instance.ToString(),
                        [Defaults.EndpointHeader] = Configuration.Settings.Endpoint,
                        [Defaults.EndpointVersionHeader] = Configuration.Settings.EndpointVersion.ToString(),
                        [Defaults.AggregatesVersionHeader] = Configuration.Settings.AggregatesVersion.ToString(),
                        [Defaults.MachineHeader] = Environment.MachineName,
                    }),
                    Compressed = _compress.HasFlag(Compression.Events),
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

                if (_compress.HasFlag(Compression.Events))
                {
                    descriptor.Compressed = true;
                    @event = @event.Compress();
                }
                var metadata = _serializer.Serialize(descriptor);

                return new EventData(
                    descriptor.EventId,
                    eventType,
                    !descriptor.Compressed,
                    @event,
                    metadata
                );
            }).ToArray();

            return DoWrite(stream, translatedEvents, expectedVersion);
        }
        public Task<long> WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, IDictionary<string, string> commitHeaders, long? expectedVersion = null) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return WriteEvents(stream, events, commitHeaders, expectedVersion);
        }

        private async Task<long> DoWrite(string stream, EventData[] events, long? expectedVersion = null)
        {
            var shard = Math.Abs(stream.GetHash() % _clients.Count());

            long nextVersion;
            using (var ctx = _metrics.Begin("EventStore Write Time"))
            {
                try
                {
                    var result = await
                        _clients[shard].AppendToStreamAsync(stream, expectedVersion ?? ExpectedVersion.Any, events)
                            .ConfigureAwait(false);

                    nextVersion = result.NextExpectedVersion;

                }
                catch (WrongExpectedVersionException e)
                {
                    throw new VersionException(e.Message, e);
                }
                catch (CannotEstablishConnectionException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
                catch (OperationTimedOutException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
                catch (EventStoreConnectionException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
                catch (InvalidOperationException e)
                {
                    throw new PersistenceException(e.Message, e);
                }

                if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                    SlowLogger.InfoEvent("SlowWrite", "{Events} events size {Size} stream [{Stream:l}] version {ExpectedVersion} took {Milliseconds} ms", events.Count(), events.Sum(x => x.Data.Length), stream, expectedVersion, ctx.Elapsed.TotalMilliseconds);
                Logger.DebugEvent("Write", "{Events} events size {Size} stream [{Stream:l}] version {ExpectedVersion} took {Milliseconds} ms", events.Count(), events.Sum(x => x.Data.Length), stream, expectedVersion, ctx.Elapsed.TotalMilliseconds);
            }
            return nextVersion;
        }

        public async Task WriteMetadata(string stream, long? maxCount = null, long? truncateBefore = null,
            TimeSpan? maxAge = null,
            TimeSpan? cacheControl = null, bool force = false, IDictionary<string, string> custom = null)
        {

            var shard = Math.Abs(stream.GetHash() % _clients.Count());

            Logger.DebugEvent("Metadata", "Metadata to stream [{Stream:l}] [ MaxCount: {MaxCount}, MaxAge: {MaxAge}, CacheControl: {CacheControl}, Custom: {Custom} ]", stream, maxCount, maxAge, cacheControl, custom.AsString());

            var existing = await _clients[shard].GetStreamMetadataAsync(stream).ConfigureAwait(false);


            var metadata = StreamMetadata.Build();

            if ((maxCount ?? existing.StreamMetadata?.MaxCount).HasValue)
                metadata.SetMaxCount((maxCount ?? existing.StreamMetadata?.MaxCount).Value);
            if ((truncateBefore ?? existing.StreamMetadata?.TruncateBefore).HasValue)
                metadata.SetTruncateBefore(Math.Max(truncateBefore ?? 0, (truncateBefore ?? existing.StreamMetadata?.TruncateBefore).Value));
            if ((maxAge ?? existing.StreamMetadata?.MaxAge).HasValue)
                metadata.SetMaxAge((maxAge ?? existing.StreamMetadata?.MaxAge).Value);
            if ((cacheControl ?? existing.StreamMetadata?.CacheControl).HasValue)
                metadata.SetCacheControl((cacheControl ?? existing.StreamMetadata?.CacheControl).Value);

            var customs = existing.StreamMetadata?.CustomKeys;
            // Make sure custom metadata is preserved
            if (customs != null && customs.Any())
            {
                foreach (var key in customs)
                    metadata.SetCustomProperty(key, existing.StreamMetadata.GetValue<string>(key));
            }

            if (custom != null)
            {
                foreach (var kv in custom)
                    metadata.SetCustomProperty(kv.Key, kv.Value);
            }

            try
            {
                try
                {
                    await _clients[shard].SetStreamMetadataAsync(stream, existing.MetastreamVersion, metadata).ConfigureAwait(false);

                }
                catch (WrongExpectedVersionException e)
                {
                    throw new VersionException(e.Message, e);
                }
                catch (CannotEstablishConnectionException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
                catch (OperationTimedOutException e)
                {
                    throw new PersistenceException(e.Message, e);
                }
                catch (EventStoreConnectionException e)
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

        public Task WriteMetadata<TEntity>(string bucket, Id streamId, Id[] parents, long? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null,
            TimeSpan? cacheControl = null, bool force = false, IDictionary<string, string> custom = null) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return WriteMetadata(stream, maxCount, truncateBefore, maxAge, cacheControl, force, custom);
        }

        public async Task<string> GetMetadata(string stream, string key)
        {
            var shard = Math.Abs(stream.GetHash() % _clients.Count());

            var existing = await _clients[shard].GetStreamMetadataAsync(stream).ConfigureAwait(false);
            
            Logger.DebugEvent("Read", "Metadata stream [{Stream:l}] {Metadata}", stream, existing.StreamMetadata?.AsJsonString());
            string property = "";
            if (!existing.StreamMetadata?.TryGetValue(key, out property) ?? false)
                property = "";
            return property;
        }
        public Task<string> GetMetadata<TEntity>(string bucket, Id streamId, Id[] parents, string key) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return GetMetadata(stream, key);
        }

    }
}
