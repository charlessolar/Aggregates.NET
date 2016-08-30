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

        public IBuilder Builder { get; set; }

        public StoreEvents(IEventStoreConnection client, IMessageMapper mapper, IStoreSnapshots snapshots, ReadOnlySettings nsbSettings, IStreamCache cache)
        {
            _client = client;
            _mapper = mapper;
            _snapshots = snapshots;
            _nsbSettings = nsbSettings;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<Boolean>("ShouldCacheEntities");
        }


        public async Task<IEventStream> GetStream<T>(String bucket, String stream, Int32? start = null) where T : class, IEntity
        {
            Logger.DebugFormat("Getting stream [{0}] in bucket [{1}]", stream, bucket);

            var streamId = String.Format("{0}.{1}", bucket, stream);
            var events = new List<ResolvedEvent>();

            var readSize = _nsbSettings.Get<Int32>("ReadSize");
            if (_shouldCache)
            {
                var cached = _cache.Retreive(streamId) as IEventStream;
                if (cached != null)
                {
                    _hitMeter.Mark();
                    Logger.DebugFormat("Found stream [{0}] bucket [{1}] in cache", stream, bucket);
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
                current = await _client.ReadStreamEventsForwardAsync(streamId, sliceStart, readSize, false);

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

            var eventstream = new Internal.EventStream<T>(Builder, this, _snapshots, bucket, stream, translatedEvents);
            if (_shouldCache)
                _cache.Cache(streamId, eventstream.Clone());

            return eventstream;
        }

        // C# doesn't support async yield and I don't want to import all of Rx
        public IEnumerable<IWritableEvent> GetEvents(String bucket, String stream, Int32? start = null, Int32? readUntil = null)
        {
            Logger.DebugFormat("Getting events from stream [{0}] in bucket [{1}]", stream, bucket);

            var streamId = String.Format("{0}.{1}", bucket, stream);
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
                current = _client.ReadStreamEventsForwardAsync(streamId, sliceStart, readSize, false).Result;

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
        public IEnumerable<IWritableEvent> GetEventsBackwards(String bucket, String stream, Int32? readUntil = null)
        {
            Logger.DebugFormat("Getting events backward from stream [{0}] in bucket [{1}]", stream, bucket);

            var streamId = String.Format("{0}.{1}", bucket, stream);
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
                current = _client.ReadStreamEventsBackwardAsync(streamId, sliceStart, readSize, false).Result;

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

        public async Task AppendEvents(String bucket, String stream, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders)
        {
            Logger.DebugFormat("Writing {0} events to stream id [{1}] bucket [{2}].  Expected version: ANY", events.Count(), stream, bucket);
            var streamId = String.Format("{0}.{1}", bucket, stream);

            if (_shouldCache)
                _cache.Evict(streamId);

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

            await _client.AppendToStreamAsync(streamId, ExpectedVersion.Any, translatedEvents);
        }

        public async Task WriteEvents(String bucket, String stream, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders)
        {
            Logger.DebugFormat("Writing {0} events to stream id [{1}] bucket [{2}].  Expected version: {3}", events.Count(), stream, bucket, expectedVersion);
            var streamId = String.Format("{0}.{1}", bucket, stream);

            if (_shouldCache)
                _cache.Evict(streamId);

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

            await _client.AppendToStreamAsync(streamId, expectedVersion, translatedEvents);
        }
    }
}
