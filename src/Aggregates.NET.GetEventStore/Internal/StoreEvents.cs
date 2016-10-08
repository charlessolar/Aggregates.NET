using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    internal class StoreEvents : IStoreEvents
    {
        private static readonly Meter HitMeter = Metric.Meter("Stream Cache Hits", Unit.Events);
        private static readonly Meter MissMeter = Metric.Meter("Stream Cache Misses", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreEvents));
        private readonly IEventStoreConnection _client;
        private readonly IMessageMapper _mapper;
        private readonly ReadOnlySettings _nsbSettings;
        private readonly IStreamCache _cache;
        private readonly bool _shouldCache;
        private readonly StreamIdGenerator _streamGen;

        public IBuilder Builder { get; set; }

        public StoreEvents(IEventStoreConnection client, IMessageMapper mapper, ReadOnlySettings nsbSettings, IStreamCache cache)
        {
            _client = client;
            _mapper = mapper;
            _nsbSettings = nsbSettings;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<bool>("ShouldCacheEntities");
            _streamGen = _nsbSettings.Get<StreamIdGenerator>("StreamGenerator");
        }

        public Task Evict<T>(string bucket, string streamId) where T: class, IEventSource
        {
            if (!_shouldCache) return Task.CompletedTask;

            var streamName = _streamGen(typeof(T), bucket, streamId);
            _cache.Evict(streamName);
            return Task.CompletedTask;
        }
        public Task Cache<T>(IEventStream stream) where T : class, IEventSource
        {
            if (!_shouldCache) return Task.CompletedTask;

            var streamName = _streamGen(typeof(T), stream.Bucket, stream.StreamId);
            _cache.Cache(streamName, stream.Clone());
            return Task.CompletedTask;
        }

        public async Task<IEventStream> GetStream<T>(string bucket, string streamId, ISnapshot snapshot = null) where T: class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            var events = new List<ResolvedEvent>();

            var sliceStart = snapshot?.Version + 1 ?? StreamPosition.Start;
            Logger.Write(LogLevel.Debug, () => $"Retreiving stream [{streamId}] in bucket [{bucket}] starting at {sliceStart}");

            var readSize = _nsbSettings.Get<int>("ReadSize");
            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as EventStream<T>;
                if (cached != null && cached.CommitVersion >= sliceStart)
                {
                    HitMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Found stream [{streamName}] in cache");
                    return new EventStream<T>(cached, Builder, this, snapshot);
                }
                MissMeter.Mark();
            }

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            StreamEventsSlice current;
            Logger.Write(LogLevel.Debug, () => $"Getting events from stream [{streamName}] starting at {sliceStart}");
            do
            {
                current = await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, readSize, false).ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Retreived {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                events.AddRange(current.Events);
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.Write(LogLevel.Debug, () => $"Finished getting events from stream [{streamName}]");

            if (current.Status == SliceReadStatus.StreamNotFound)
            {
                Logger.Write(LogLevel.Warn, () => $"Stream [{streamName}] does not exist!");
                return null;
            }

            var compress = _nsbSettings.Get<bool>("Compress");

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
                return new WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            });

            var eventstream = new EventStream<T>(Builder, this, bucket, streamId, translatedEvents, snapshot);
            if (_shouldCache)
                await Cache<T>(eventstream);

            return eventstream;
        }

        public Task<IEventStream> NewStream<T>(string bucket, string streamId) where T : class, IEventSource
        {
            Logger.Write(LogLevel.Debug, () => $"Creating new stream [{streamId}] in bucket [{bucket}]");
            IEventStream stream = new EventStream<T>(Builder, this, bucket, streamId, null, null);
            return Task.FromResult(stream);
        }

        public async Task<IEnumerable<IWritableEvent>> GetEvents<T>(string bucket, string streamId, int? start = null, int? count = null) where T : class, IEventSource
        {

            var streamName = _streamGen(typeof(T), bucket, streamId);
            var readSize = _nsbSettings.Get<int>("ReadSize");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            var events = new List<ResolvedEvent>();
            StreamEventsSlice current;
            var sliceStart = start ?? StreamPosition.Start;
            Logger.Write(LogLevel.Debug, () => $"Getting events from stream [{streamName}] starting at {sliceStart}");
            do
            {
                var take = Math.Min((count ?? int.MaxValue) - events.Count, readSize);
                current = await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, take, false).ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Retreived {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                events.AddRange(current.Events);

                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.Write(LogLevel.Debug, () => $"Finished getting events from stream [{streamName}]");

            var compress = _nsbSettings.Get<bool>("Compress");

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
                return new WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            });

            return translatedEvents;
        }
        public async Task<IEnumerable<IWritableEvent>> GetEventsBackwards<T>(string bucket, string streamId, int? start = null, int? count = null) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            var readSize = _nsbSettings.Get<int>("ReadSize");

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

            Logger.Write(LogLevel.Debug, () => $"Getting events backwards from stream [{streamName}] starting at {sliceStart}");
            do
            {
                var take = Math.Min((count ?? int.MaxValue) - events.Count, readSize);
                current = await _client.ReadStreamEventsBackwardAsync(streamName, sliceStart, take, false).ConfigureAwait(false);
                Logger.Write(LogLevel.Debug, () => $"Retreived backwards {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                events.AddRange(current.Events);

                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.Write(LogLevel.Debug, () => $"Finished getting all events backward from stream [{streamName}]");

            var compress = _nsbSettings.Get<bool>("Compress");

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
                return new WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            });

            return translatedEvents;
        }

        public async Task AppendEvents<T>(string bucket, string streamId, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Writing {events.Count()} events to stream [{streamName}].  Expected version: ANY");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper)
            };

            var compress = _nsbSettings.Get<bool>("Compress");

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
                    e.EventId ?? Guid.NewGuid(),
                    mappedType.AssemblyQualifiedName,
                    !compress,
                    @event,
                    metadata
                    );
            }).ToList();

            await _client.AppendToStreamAsync(streamName, ExpectedVersion.Any, translatedEvents).ConfigureAwait(false);
        }

        public async Task WriteEvents<T>(string bucket, string streamId, int expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Writing {events.Count()} events to stream id [{streamName}].  Expected version: {expectedVersion}");
            
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper)
            };

            var compress = _nsbSettings.Get<bool>("Compress");

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
                    e.EventId ?? Guid.NewGuid(),
                    mappedType.AssemblyQualifiedName,
                    !compress,
                    @event,
                    metadata
                    );
            }).ToList();

            await _client.AppendToStreamAsync(streamName, expectedVersion, translatedEvents).ConfigureAwait(false);
        }


        public async Task WriteEventMetadata<T>(string bucket, string streamId, int? maxCount = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Writing metadata [ {nameof(maxCount)}: {maxCount}, {nameof(maxAge)}: {maxAge}, {nameof(cacheControl)}: {cacheControl} ] to stream [{streamName}]");

            var metadata = StreamMetadata.Create(maxCount: maxCount, maxAge: maxAge, cacheControl: cacheControl);

            await _client.SetStreamMetadataAsync(streamName, ExpectedVersion.Any, metadata).ConfigureAwait(false);
        }
    }
}
