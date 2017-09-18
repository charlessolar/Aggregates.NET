using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    class StoreEvents : IStoreEvents
    {
        private static readonly ILog Logger = LogProvider.GetLogger("StoreEvents");
        private static readonly ILog SlowLogger = LogProvider.GetLogger("Slow Alarm");
        private readonly IMetrics _metrics;
        private readonly IMessageSerializer _serializer;
        private readonly IEventMapper _mapper;
        private readonly StreamIdGenerator _generator;
        private readonly IEventStoreConnection[] _clients;
        private readonly int _readsize;
        private readonly Compression _compress;

        public StoreEvents(IMetrics metrics, IMessageSerializer serializer, IEventMapper mapper, StreamIdGenerator generator, int readsize, Compression compress, IEventStoreConnection[] connections)
        {
            _metrics = metrics;
            _serializer = serializer;
            _mapper = mapper;
            _generator = generator;
            _clients = connections;
            _readsize = readsize;
            _compress = compress;
        }

        public Task<IFullEvent[]> GetEvents<TEntity>(string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.Domain, bucket, streamId, parents);
            return GetEvents(stream, start, count);
        }

        public async Task<IFullEvent[]> GetEvents(string stream, long? start = null, int? count = null)
        { 
            var shard = Math.Abs(stream.GetHashCode() % _clients.Count());

            var sliceStart = start ?? StreamPosition.Start;
            StreamEventsSlice current;
            Logger.Write(LogLevel.Debug, () => $"Reading events from stream [{stream}] starting at {sliceStart}");

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

                    Logger.Write(LogLevel.Debug,
                        () => $"Read {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                    events.AddRange(current.Events);
                    sliceStart = current.NextEventNumber;
                } while (!current.IsEndOfStream && (!count.HasValue || (events.Count != count.Value)));

                if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                    SlowLogger.Write(LogLevel.Warn, () => $"Reading {events.Count} events of total size {events.Sum(x => x.Event.Data.Length)} from stream [{stream}] took {ctx.Elapsed.TotalSeconds} seconds!");
                Logger.Write(LogLevel.Info, () => $"Reading {events.Count} events of total size {events.Sum(x => x.Event.Data.Length)} from stream [{stream}] took {ctx.Elapsed.TotalMilliseconds} ms");
            }
            Logger.Write(LogLevel.Debug, () => $"Finished reading {events.Count} events from stream [{stream}]");

            if (current.Status == SliceReadStatus.StreamNotFound)
            {
                Logger.Write(LogLevel.Info, () => $"Stream [{stream}] does not exist!");
                throw new NotFoundException($"Stream [{stream}] does not exist!");
            }
            
            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;

                var descriptor = _serializer.Deserialize<EventDescriptor>(metadata);

                if (descriptor.Compressed)
                    data = data.Decompress();

                var @event = _serializer.Deserialize(e.Event.EventType, data);

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

            Logger.Write(LogLevel.Info, () => $"Read {translatedEvents.Length} events from stream [{stream}]");
            return translatedEvents;
        }

        public async Task<IFullEvent[]> GetEventsBackwards(string stream, long? start = null, int? count = null)
        {

            var shard = Math.Abs(stream.GetHashCode() % _clients.Count());

            var events = new List<ResolvedEvent>();
            var sliceStart = StreamPosition.End;

            StreamEventsSlice current;
            Logger.Write(LogLevel.Debug, () => $"Reading events from stream [{stream}] starting at {sliceStart}");
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
                Logger.Write(LogLevel.Debug,
                    () => $"Reading events backwards from stream [{stream}] starting at {sliceStart}");

                using (var ctx = _metrics.Begin("EventStore Read Time"))
                {
                    do
                    {
                        var take = Math.Min((count ?? int.MaxValue) - events.Count, _readsize);

                        current =
                            await _clients[shard].ReadStreamEventsBackwardAsync(stream, sliceStart, take, false)
                                .ConfigureAwait(false);

                        Logger.Write(LogLevel.Debug,
                            () =>
                                $"Read backwards {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                        events.AddRange(current.Events);

                        sliceStart = current.NextEventNumber;
                    } while (!current.IsEndOfStream);

                    if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                        SlowLogger.Write(LogLevel.Warn, () => $"Reading {events.Count} events of total size {events.Sum(x => x.Event.Data.Length)} from stream [{stream}] took {ctx.Elapsed.TotalSeconds} seconds!");
                    Logger.Write(LogLevel.Info, () => $"Reading {events.Count} events of total size {events.Sum(x => x.Event.Data.Length)} from stream [{stream}] took {ctx.Elapsed.TotalMilliseconds} ms!");
                }
                Logger.Write(LogLevel.Debug, () => $"Finished reading {events.Count} events backward from stream [{stream}]");

                if (current.Status == SliceReadStatus.StreamNotFound)
                {
                    Logger.Write(LogLevel.Info, () => $"Stream [{stream}] does not exist!");
                    throw new NotFoundException($"Stream [{stream}] does not exist!");
                }
            }

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;


                var descriptor = _serializer.Deserialize<EventDescriptor>(metadata);

                if (descriptor.Compressed)
                    data = data.Decompress();

                var @event = _serializer.Deserialize(e.Event.EventType, data);

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

            Logger.Write(LogLevel.Info, () => $"Read {translatedEvents.Length} events backwards from stream [{stream}]");
            return translatedEvents;
        }
        public Task<IFullEvent[]> GetEventsBackwards<TEntity>(string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.Domain, bucket, streamId, parents);
            return GetEventsBackwards(stream, start, count);
        }

        public async Task<bool> VerifyVersion(string stream, long expectedVersion)
        {
            var size = await Size(stream);
            return size == expectedVersion;
        }
        public async Task<bool> VerifyVersion<TEntity>(string bucket, Id streamId, Id[] parents,
            long expectedVersion) where TEntity : IEntity
        {
            var size = await Size<TEntity>(bucket, streamId, parents);
            return size == expectedVersion;
        }


        public async Task<long> Size(string stream)
        {
            Logger.Write(LogLevel.Debug, () => $"Getting the size of stream {stream}");

            var shard = Math.Abs(stream.GetHashCode() % _clients.Count());

            var result = await _clients[shard].ReadStreamEventsBackwardAsync(stream, StreamPosition.End, 1, false).ConfigureAwait(false);
            return result.Status == SliceReadStatus.Success ? result.NextEventNumber : 0;

        }
        public Task<long> Size<TEntity>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.Domain, bucket, streamId, parents);
            return Size(stream);
        }

        public Task<long> WriteEvents(string stream, IFullEvent[] events,
            IDictionary<string, string> commitHeaders, long? expectedVersion = null)
        {

            Logger.Write(LogLevel.Debug, () => $"Writing {events.Count()} events to stream id [{stream}].  Expected version: {expectedVersion}");
            
            var translatedEvents = events.Select(e =>
            {
                var descriptor = new EventDescriptor
                {
                    EventId = e.EventId ?? Guid.NewGuid(),
                    CommitHeaders = commitHeaders ?? new Dictionary<string, string>(),
                    Compressed = _compress.HasFlag(Compression.Events),
                    EntityType = e.Descriptor.EntityType,
                    StreamType = e.Descriptor.StreamType,
                    Bucket = e.Descriptor.Bucket,
                    StreamId = e.Descriptor.StreamId,
                    Parents = e.Descriptor.Parents,
                    Version = e.Descriptor.Version,
                    Timestamp = e.Descriptor.Timestamp,
                    Headers = e.Descriptor.Headers
                };

                var mappedType = e.Event.GetType();
                if (!mappedType.IsInterface)
                    mappedType = _mapper.GetMappedTypeFor(mappedType) ?? mappedType;

                var @event = _serializer.Serialize(e.Event);

                if (_compress.HasFlag(Compression.Events))
                {
                    descriptor.Compressed = true;
                    @event = @event.Compress();
                }
                var metadata = _serializer.Serialize(descriptor);

                return new EventData(
                    descriptor.EventId,
                    mappedType.AssemblyQualifiedName,
                    !descriptor.Compressed,
                    @event,
                    metadata
                );
            }).ToArray();

            return DoWrite(stream, translatedEvents, expectedVersion);
        }
        public Task<long> WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, IDictionary<string, string> commitHeaders, long? expectedVersion = null) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.Domain, bucket, streamId, parents);
            return WriteEvents(stream, events, commitHeaders, expectedVersion);
        }

        private async Task<long> DoWrite(string stream, EventData[] events, long? expectedVersion = null)
        {
            var shard = Math.Abs(stream.GetHashCode() % _clients.Count());

            long nextVersion;
            using (var ctx = _metrics.Begin("EventStore Write Time"))
            {
                EventStoreTransaction transaction = null;
                try
                {
                    if (events.Count() > _readsize)
                        transaction = await _clients[shard].StartTransactionAsync(stream, expectedVersion ?? ExpectedVersion.Any).ConfigureAwait(false);

                    if (transaction != null)
                    {
                        Logger.Write(LogLevel.Debug, () => $"Using transaction {events.Count()} is over max {_readsize} to write stream id [{stream}]");
                        var page = 0;
                        while (page < events.Count())
                        {
                            await transaction.WriteAsync(events.Skip(page).Take(_readsize)).ConfigureAwait(false);
                            page += _readsize;
                        }
                        var result = await transaction.CommitAsync().ConfigureAwait(false);
                        nextVersion = result.NextExpectedVersion;
                    }
                    else
                    {
                        var result = await
                            _clients[shard].AppendToStreamAsync(stream, expectedVersion ?? ExpectedVersion.Any, events)
                                .ConfigureAwait(false);

                        nextVersion = result.NextExpectedVersion;
                    }
                }
                catch (WrongExpectedVersionException e)
                {
                    transaction?.Rollback();
                    throw new VersionException($"We expected version {expectedVersion ?? ExpectedVersion.Any}", e);
                }
                catch (CannotEstablishConnectionException e)
                {
                    transaction?.Rollback();
                    throw new PersistenceException(e.Message, e);
                }
                catch (OperationTimedOutException e)
                {
                    transaction?.Rollback();
                    throw new PersistenceException(e.Message, e);
                }
                catch (EventStoreConnectionException e)
                {
                    transaction?.Rollback();
                    throw new PersistenceException(e.Message, e);
                }
                
                if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                    SlowLogger.Write(LogLevel.Warn, () => $"Writing {events.Count()} events of total size {events.Sum(x => x.Data.Length)} to stream [{stream}] version {expectedVersion} took {ctx.Elapsed.TotalSeconds} seconds!");
                Logger.Write(LogLevel.Debug, () => $"Writing {events.Count()} events of total size {events.Sum(x => x.Data.Length)} to stream [{stream}] version {expectedVersion} took {ctx.Elapsed.TotalMilliseconds} ms");
            }
            return nextVersion;
        }
        
        public async Task WriteMetadata(string stream, long? maxCount = null, long? truncateBefore = null,
            TimeSpan? maxAge = null,
            TimeSpan? cacheControl = null, bool force = false, IDictionary<string, string> custom = null)
        {

            var shard = Math.Abs(stream.GetHashCode() % _clients.Count());

            Logger.Write(LogLevel.Debug, () => $"Writing metadata to stream [{stream}] [ {nameof(maxCount)}: {maxCount}, {nameof(maxAge)}: {maxAge}, {nameof(cacheControl)}: {cacheControl}, {nameof(custom)}: {custom.AsString()} ]");

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
                Logger.Write(LogLevel.Debug, () => $"Writing metadata to stream [{stream}] version {existing.MetastreamVersion} ");
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

        public Task WriteMetadata<TEntity>(string bucket, Id streamId, Id[] parents, long? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null,
            TimeSpan? cacheControl = null, bool force = false, IDictionary<string, string> custom = null) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.Domain, bucket, streamId, parents);
            return WriteMetadata(stream, maxCount, truncateBefore, maxAge, cacheControl, force, custom);
        }

        public async Task<string> GetMetadata(string stream, string key)
        {
            var shard = Math.Abs(stream.GetHashCode() % _clients.Count());
            Logger.Write(LogLevel.Debug, () => $"Getting metadata '{key}' from stream [{stream}]");

            var existing = await _clients[shard].GetStreamMetadataAsync(stream).ConfigureAwait(false);

            if (existing.StreamMetadata == null)
                Logger.Write(LogLevel.Debug, () => $"No metadata exists for stream [{stream}]");

            Logger.Write(LogLevel.Debug,
                () => $"Read metadata from stream [{stream}] - {existing.StreamMetadata?.AsJsonString()}");
            string property = "";
            if (!existing.StreamMetadata?.TryGetValue(key, out property) ?? false)
                property = "";
            return property;
        }
        public Task<string> GetMetadata<TEntity>(string bucket, Id streamId, Id[] parents, string key) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.Domain, bucket, streamId, parents);
            return GetMetadata(stream, key);
        }
        
    }
}
