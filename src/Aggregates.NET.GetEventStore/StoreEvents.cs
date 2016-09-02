using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Metrics;
using Newtonsoft.Json;
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
        private readonly IStoreSnapshots _snapshots;
        private readonly ReadOnlySettings _nsbSettings;
        private readonly IStreamCache _cache;
        private readonly Boolean _shouldCache;
        private readonly StreamIdGenerator _streamGen;

        public IBuilder Builder { get; set; }

        public StoreEvents(IEventStoreConnection client, IMessageMapper mapper, IStoreSnapshots snapshots, ReadOnlySettings nsbSettings, IStreamCache cache)
        {
            _client = client;
            _mapper = mapper;
            _snapshots = snapshots;
            _nsbSettings = nsbSettings;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<Boolean>("ShouldCacheEntities");
            _streamGen = _nsbSettings.Get<StreamIdGenerator>("StreamGenerator");
        }


        public async Task<IEventStream> GetStream<T>(String bucket, String streamId, Int32? start = null) where T : class, IEventSource
        {
            Logger.DebugFormat("Getting stream [{0}] in bucket [{1}] for type {2}", streamId, bucket, typeof(T).FullName);
            
            var streamName = _streamGen(typeof(T), bucket, streamId);
            var events = new List<ResolvedEvent>();

            var readSize = _nsbSettings.Get<Int32>("ReadSize");
            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamName) as IEventStream;
                if (cached != null)
                {
                    _hitMeter.Mark();
                    Logger.DebugFormat("Found stream [{0}] bucket [{1}] in cache", streamId, bucket);
                    return new Internal.EventStream<T>(cached, Builder, this, _snapshots);
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
            do
            {
                current = await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, readSize, false);

                events.AddRange(current.Events);
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);

            var translatedEvents = events.Select(e =>
            {
                var descriptor = e.Event.Metadata.Deserialize(settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, settings);

                return new Internal.WritableEvent
                {
                    Descriptor = descriptor,
                    Event = data,
                    EventId = e.Event.EventId
                };
            });

            var eventstream = new Internal.EventStream<T>(Builder, this, _snapshots, bucket, streamId, current.LastEventNumber, translatedEvents);
            if (_shouldCache)
                _cache.Cache(streamName, eventstream.Clone());

            return eventstream;
        }

        // C# doesn't support async yield and I don't want to import all of Rx
        public IEnumerable<IWritableEvent> GetEvents<T>(String bucket, String streamId, Int32? start = null, Int32? readUntil = null) where T : class, IEventSource
        {
            Logger.DebugFormat("Getting events from stream [{0}] in bucket [{1}] for type {2}", streamId, bucket, typeof(T).FullName);

            var streamName = _streamGen(typeof(T), bucket, streamId);
            var readSize = _nsbSettings.Get<Int32>("ReadSize");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };
            
            StreamEventsSlice current;
            var sliceStart = start ?? StreamPosition.Start;
            do
            {
                current = _client.ReadStreamEventsForwardAsync(streamName, sliceStart, readSize, false).Result;

                foreach (var e in current.Events)
                {
                    if (readUntil.HasValue && e.OriginalEventNumber > readUntil)
                        break;

                    var descriptor = e.Event.Metadata.Deserialize(settings);
                    var data = e.Event.Data.Deserialize(e.Event.EventType, settings);

                    yield return new Internal.WritableEvent
                    {
                        Descriptor = descriptor,
                        Event = data,
                        EventId = e.Event.EventId
                    };        
                }

                if (readUntil.HasValue && current.LastEventNumber >= readUntil)
                    break;
                
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
            
        }
        public IEnumerable<IWritableEvent> GetEventsBackwards<T>(String bucket, String streamId, Int32? readUntil = null) where T : class, IEventSource
        {
            Logger.DebugFormat("Getting events backward from stream [{0}] in bucket [{1}] for type {2}", streamId, bucket, typeof(T).FullName);

            var streamName = _streamGen(typeof(T), bucket, streamId);
            var readSize = _nsbSettings.Get<Int32>("ReadSize");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };
            
            StreamEventsSlice current;
            var sliceStart = StreamPosition.End;
            do
            {
                current = _client.ReadStreamEventsBackwardAsync(streamName, sliceStart, readSize, false).Result;

                foreach (var e in current.Events)
                {
                    if (readUntil.HasValue && e.OriginalEventNumber < readUntil)
                        break;

                    var descriptor = e.Event.Metadata.Deserialize(settings);
                    var data = e.Event.Data.Deserialize(e.Event.EventType, settings);

                    yield return new Internal.WritableEvent
                    {
                        Descriptor = descriptor,
                        Event = data,
                        EventId = e.Event.EventId
                    };
                }

                if (readUntil.HasValue && current.LastEventNumber <= readUntil)
                    break;

                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);
        }

        public async Task AppendEvents<T>(String bucket, String streamId, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders) where T : class, IEventSource
        {
            Logger.DebugFormat("Writing {0} events to stream id [{1}] bucket [{2}] for type {3}.  Expected version: ANY", events.Count(), streamId, bucket, typeof(T).FullName);
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
            Logger.DebugFormat("Writing {0} events to stream id [{1}] bucket [{2}] for type {2}.  Expected version: {3}", events.Count(), streamId, bucket, expectedVersion, typeof(T).FullName);
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
