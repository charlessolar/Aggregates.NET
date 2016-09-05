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
                    Logger.WriteFormat(LogLevel.Debug, "Found stream [{0}] bucket [{1}] in cache", streamId, bucket);
                    return new Internal.EventStream<T>(cached, Builder, this);
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
            Logger.WriteFormat(LogLevel.Debug, "Getting events from stream [{0}] in bucket [{1}] for type {2} starting at {3}", streamId, bucket, typeof(T).FullName, sliceStart);
            do
            {
                current = await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, readSize, false);
                Logger.WriteFormat(LogLevel.Debug, "Retreived {0} events from position {1}. Status: {4} LastEventNumber: {2} NextEventNumber: {3}", current.Events.Count(), sliceStart, current.LastEventNumber, current.LastEventNumber, current.Status);

                events.AddRange(current.Events);
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.WriteFormat(LogLevel.Debug, "Finished getting events from stream [{0}] in bucket [{1}] for type {2}", streamId, bucket, typeof(T).FullName);

            var translatedEvents = events.Select(e =>
            {
                var descriptor = e.Event.Metadata.Deserialize(settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, settings);

                var @event = data as IEvent;
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
            Logger.WriteFormat(LogLevel.Debug, "Getting events from stream [{0}] in bucket [{1}] for type {2} starting at {3}", streamId, bucket, typeof(T).FullName, sliceStart);
            do
            {
                var take = Math.Min((count ?? Int32.MaxValue) - events.Count, readSize);
                current = await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, take, false);
                Logger.WriteFormat(LogLevel.Debug, "Retreived {0} events from position {1}. Status: {4} LastEventNumber: {2} NextEventNumber: {3}", current.Events.Count(), sliceStart, current.LastEventNumber, current.LastEventNumber, current.Status);

                events.AddRange(current.Events);
                
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.WriteFormat(LogLevel.Debug, "Finished getting events from stream [{0}] in bucket [{1}] for type {2}", streamId, bucket, typeof(T).FullName);

            var translatedEvents = events.Select(e =>
            {
                var descriptor = e.Event.Metadata.Deserialize(settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, settings);

                var @event = data as IEvent;
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
        public async Task<IEnumerable<IWritableEvent>> GetEventsBackwards<T>(String bucket, String streamId, Int32? count = null) where T : class, IEventSource
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

            Logger.WriteFormat(LogLevel.Debug, "Getting events backwards from stream [{0}] in bucket [{1}] for type {2} starting at {3}", streamId, bucket, typeof(T).FullName, sliceStart);
            do
            {
                var take = Math.Min((count ?? Int32.MaxValue) - events.Count, readSize);
                current = await _client.ReadStreamEventsBackwardAsync(streamName, sliceStart, take, false);
                Logger.WriteFormat(LogLevel.Debug, "Retreived backwards {0} events from position {1}. Status: {4} LastEventNumber: {2} NextEventNumber: {3}", current.Events.Count(), sliceStart, current.LastEventNumber, current.LastEventNumber, current.Status);

                events.AddRange(current.Events);

                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            Logger.WriteFormat(LogLevel.Debug, "Finished getting all events backward from stream [{0}] in bucket [{1}] for type {2}", streamId, bucket, typeof(T).FullName);

            var translatedEvents = events.Select(e =>
            {
                var descriptor = e.Event.Metadata.Deserialize(settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, settings);

                var @event = data as IEvent;
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
            Logger.WriteFormat(LogLevel.Debug, "Writing {0} events to stream id [{1}] bucket [{2}] for type {3}.  Expected version: ANY", events.Count(), streamId, bucket, typeof(T).FullName);
            var streamName = _streamGen(typeof(T), bucket, streamId);

            if (_shouldCache)
                _cache.Evict(streamName);

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper)
            };

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


                return new EventData(
                    e.EventId,
                    mappedType.AssemblyQualifiedName,
                    true,
                    e.Event.Serialize(settings).AsByteArray(),
                    descriptor.Serialize(settings).AsByteArray()
                    );
            }).ToList();

            await _client.AppendToStreamAsync(streamName, ExpectedVersion.Any, translatedEvents);
        }

        public async Task WriteEvents<T>(String bucket, String streamId, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders) where T : class, IEventSource
        {
            Logger.WriteFormat(LogLevel.Debug, "Writing {0} events to stream id [{1}] bucket [{2}] for type {2}.  Expected version: {3}", events.Count(), streamId, bucket, expectedVersion, typeof(T).FullName);
            var streamName = _streamGen(typeof(T), bucket, streamId);

            if (_shouldCache)
                _cache.Evict(streamName);

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper)
            };

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


                return new EventData(
                    e.EventId,
                    mappedType.AssemblyQualifiedName,
                    true,
                    e.Event.Serialize(settings).AsByteArray(),
                    descriptor.Serialize(settings).AsByteArray()
                    );
            }).ToList();

            await _client.AppendToStreamAsync(streamName, expectedVersion, translatedEvents);
        }
    }
}
