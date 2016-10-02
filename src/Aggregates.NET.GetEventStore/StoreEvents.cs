using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class StoreEvents : IStoreEvents
    {
        private static Meter _hitMeter = Metric.Meter("Stream Cache Hits", Unit.Events);
        private static Meter _missMeter = Metric.Meter("Stream Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreEvents));
        private readonly IEventStoreConnection _client;
        private readonly IMessageMapper _mapper;
        private readonly ReadOnlySettings _nsbSettings;
        private readonly IStreamCache _cache;
        private readonly Boolean _shouldCache;
        private readonly StreamIdGenerator _streamGen;

        public IBuilder Builder { get; set; }

        public StoreEvents(IEventStoreConnection client, IMessageMapper mapper, ReadOnlySettings nsbSettings, IStreamCache cache)
        {
            _client = client;
            _mapper = mapper;
            _nsbSettings = nsbSettings;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<Boolean>("ShouldCacheEntities");
            _streamGen = _nsbSettings.Get<StreamIdGenerator>("StreamGenerator");
        }


        public async Task<IEventStream> GetStream<T>(String bucket, String streamId, Int32? start = null) where T : class, IEventSource
        {

            var streamName = _streamGen(typeof(T), bucket, streamId);
            var events = new List<ResolvedEvent>();

            var readSize = _nsbSettings.Get<Int32>("ReadSize");
            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as IEventStream;
                if (cached != null)
                {
                    _hitMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Found stream [{streamId}] bucket [{bucket}] in cache");
                    var stream = new Internal.EventStream<T>(cached, Builder, this);
                    stream.TrimEvents(start);
                    return stream;
                }
                _missMeter.Mark();
            }

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            StreamEventsSlice current;
            var sliceStart = start ?? StreamPosition.Start;
            Logger.Write(LogLevel.Debug, () => $"Getting events from stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName} starting at {sliceStart}");
            do
            {
                current = await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, readSize, false).ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Retreived {current.Events.Count()} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                events.AddRange(current.Events);
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.Write(LogLevel.Debug, () => $"Finished getting events from stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName}");

            var compress = _nsbSettings.Get<Boolean>("Compress");

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;
                if (compress)
                {
                    metadata = metadata.Decompress();
                    data = data.Decompress();
                }

                var descriptor = metadata.Deserialize(settings);
                var @event = data.Deserialize(e.Event.EventType, settings) as IEvent;
                
                if (@event == null)
                    throw new InvalidOperationException($"Event type {e.Event.EventType} on stream {streamName} does not inherit from IEvent and therefore cannot be read");
                return new Internal.WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            });

            var eventstream = new Internal.EventStream<T>(Builder, this, bucket, streamId, current.LastEventNumber, translatedEvents);
            if (_shouldCache)
                _cache.Cache(streamName, eventstream.Clone());

            return eventstream;
        }
        
        public async Task<IEnumerable<IWritableEvent>> GetEvents<T>(String bucket, String streamId, Int32? start = null, Int32? count = null) where T : class, IEventSource
        {

            var streamName = _streamGen(typeof(T), bucket, streamId);
            var readSize = _nsbSettings.Get<Int32>("ReadSize");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            var events = new List<ResolvedEvent>();
            StreamEventsSlice current;
            var sliceStart = start ?? StreamPosition.Start;
            Logger.Write(LogLevel.Debug, () => $"Getting events from stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName} starting at {sliceStart}");
            do
            {
                var take = Math.Min((count ?? Int32.MaxValue) - events.Count, readSize);
                current = await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, take, false).ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Retreived {current.Events.Count()} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                events.AddRange(current.Events);
                
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.Write(LogLevel.Debug, () => $"Finished getting events from stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName}");

            var compress = _nsbSettings.Get<Boolean>("Compress");

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;
                if (compress)
                {
                    metadata = metadata.Decompress();
                    data = data.Decompress();
                }

                var descriptor = metadata.Deserialize(settings);
                var @event = data.Deserialize(e.Event.EventType, settings) as IEvent;

                if (@event == null)
                    throw new InvalidOperationException($"Event type {e.Event.EventType} on stream {streamName} does not inherit from IEvent and therefore cannot be read");
                return new Internal.WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            });

            return translatedEvents;
        }
        public async Task<IEnumerable<IWritableEvent>> GetEventsBackwards<T>(String bucket, String streamId, Int32? start = null, Int32? count = null) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            var readSize = _nsbSettings.Get<Int32>("ReadSize");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            var events = new List<ResolvedEvent>();
            StreamEventsSlice current;
            var sliceStart = StreamPosition.End;

            if (start.HasValue)
            {
                // Interesting, ReadStreamEventsBackwardAsync's [start] parameter marks start from begining of stream, not an offset from the end.
                // Read 1 event from the end, to figure out where start should be
                var result = await _client.ReadStreamEventsBackwardAsync(streamName, StreamPosition.End, 1, false).ConfigureAwait(false);
                sliceStart = result.NextEventNumber - start.Value;
            }

            Logger.Write(LogLevel.Debug, () => $"Getting events backwards from stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName} starting at {sliceStart}");
            do
            {
                var take = Math.Min((count ?? Int32.MaxValue) - events.Count, readSize);
                current = await _client.ReadStreamEventsBackwardAsync(streamName, sliceStart, take, false).ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Retreived backwards {current.Events.Count()} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                events.AddRange(current.Events);

                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.Write(LogLevel.Debug, () => $"Finished getting all events backward from stream [{streamId}] in bucket [{bucket}] for type {typeof(T).FullName}");

            var compress = _nsbSettings.Get<Boolean>("Compress");

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;
                if (compress)
                {
                    metadata = metadata.Decompress();
                    data = data.Decompress();
                }

                var descriptor = metadata.Deserialize(settings);
                var @event = data.Deserialize(e.Event.EventType, settings) as IEvent;

                if (@event == null)
                    throw new InvalidOperationException($"Event type {e.Event.EventType} on stream {streamName} does not inherit from IEvent and therefore cannot be read");
                return new Internal.WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            });

            return translatedEvents;
        }

        public async Task AppendEvents<T>(String bucket, String streamId, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders) where T : class, IEventSource
        {
            Logger.Write(LogLevel.Debug, () => $"Writing {events.Count()} events to stream id [{streamId}] bucket [{bucket}] for type {typeof(T).FullName}.  Expected version: ANY");
            var streamName = _streamGen(typeof(T), bucket, streamId);
            
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper)
            };

            var compress = _nsbSettings.Get<Boolean>("Compress");

            var translatedEvents = events.Select(e =>
            {

                var descriptor = new EventDescriptor
                {
                    EntityType = e.Descriptor.EntityType,
                    Timestamp = e.Descriptor.Timestamp,
                    Version = e.Descriptor.Version,
                    Headers = commitHeaders.Merge(e.Descriptor.Headers)
                };

                var mappedType = _mapper.GetMappedTypeFor(e.Event.GetType());

                var @event = e.Event.Serialize(settings).AsByteArray();
                var metadata = descriptor.Serialize(settings).AsByteArray();
                if (compress)
                {
                    @event = @event.Compress();
                    metadata = metadata.Compress();
                }

                return new EventData(
                    e.EventId,
                    mappedType.AssemblyQualifiedName,
                    !compress,
                    @event,
                    metadata
                    );
            }).ToList();

            await _client.AppendToStreamAsync(streamName, ExpectedVersion.Any, translatedEvents).ConfigureAwait(false);
        }

        public async Task WriteEvents<T>(String bucket, String streamId, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders) where T : class, IEventSource
        {
            Logger.Write(LogLevel.Debug, () => $"Writing {events.Count()} events to stream id [{streamId}] bucket [{bucket}] for type {typeof(T).FullName}.  Expected version: {expectedVersion}");
            var streamName = _streamGen(typeof(T), bucket, streamId);

            if (_shouldCache)
                _cache.Evict(streamName);

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper)
            };

            var compress = _nsbSettings.Get<Boolean>("Compress");

            var translatedEvents = events.Select(e =>
            {

                var descriptor = new EventDescriptor
                {
                    EntityType = e.Descriptor.EntityType,
                    Timestamp = e.Descriptor.Timestamp,
                    Version = e.Descriptor.Version,
                    Headers = commitHeaders.Merge(e.Descriptor.Headers)
                };

                var mappedType = _mapper.GetMappedTypeFor(e.Event.GetType());


                var @event = e.Event.Serialize(settings).AsByteArray();
                var metadata = descriptor.Serialize(settings).AsByteArray();
                if (compress)
                {
                    @event = @event.Compress();
                    metadata = metadata.Compress();
                }

                return new EventData(
                    e.EventId,
                    mappedType.AssemblyQualifiedName,
                    !compress,
                    @event,
                    metadata
                    );
            }).ToList();

            await _client.AppendToStreamAsync(streamName, expectedVersion, translatedEvents).ConfigureAwait(false);
        }


        public async Task WriteEventMetadata<T>(String bucket, String streamId, Int32? MaxCount = null, TimeSpan? MaxAge = null, TimeSpan? CacheControl = null) where T : class, IEventSource
        {
            Logger.Write(LogLevel.Debug, () => $"Writing metadata to stream id [{streamId}] bucket [{bucket}] for type {typeof(T).FullName}");
            var streamName = _streamGen(typeof(T), bucket, streamId);

            var metadata = StreamMetadata.Create(maxCount: MaxCount, maxAge: MaxAge, cacheControl: CacheControl);

            await _client.SetStreamMetadataAsync(streamName, ExpectedVersion.Any, metadata).ConfigureAwait(false);
        }
    }
}
