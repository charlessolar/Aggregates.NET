using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Internal;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
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
        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreEvents));
        private readonly IEventStoreConnection _client;
        private readonly IMessageMapper _mapper;
        private readonly IStoreSnapshots _snapshots;
        private readonly IBuilder _builder;
        private readonly ReadOnlySettings _nsbSettings;
        private readonly IStreamCache _cache;
        private readonly Boolean _shouldCache;

        public StoreEvents(IEventStoreConnection client, IBuilder builder, IMessageMapper mapper, IStoreSnapshots snapshots, ReadOnlySettings nsbSettings, IStreamCache cache)
        {
            _client = client;
            _mapper = mapper;
            _snapshots = snapshots;
            _nsbSettings = nsbSettings;
            _builder = builder;
            _cache = cache;
            _shouldCache = _nsbSettings.Get<Boolean>("ShouldCacheEntities");
        }


        public async Task<IEventStream> GetStream<T>(String bucket, String stream, Int32? start = null) where T : class, IEntity
        {
            Logger.DebugFormat("Getting stream '{0}' in bucket '{1}'", stream, bucket);

            var streamId = String.Format("{0}.{1}", bucket, stream);
            var events = new List<ResolvedEvent>();

            var readSize = _nsbSettings.Get<Int32>("ReadSize");
            if(_shouldCache)
            {
                var cached = _cache.Retreive(streamId);
                if (cached != null)
                    return cached;
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
            
            var eventstream = new Internal.EventStream<T>(_builder, this, _snapshots, bucket, stream, current.LastEventNumber, translatedEvents);
            if(_shouldCache)
                _cache.Cache(streamId, eventstream.Clone());

            return eventstream;
        }

        public async Task WriteEvents(String bucket, String stream, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders)
        {
            Logger.DebugFormat("Writing {0} events to stream id '{1}'.  Expected version: {2}", events.Count(), stream, expectedVersion);
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
                    Headers = e.Descriptor.Headers.Merge(commitHeaders)
                };

                var mappedType = _mapper.GetMappedTypeFor(e.Event.GetType());


                return new EventData(
                    e.EventId,
                    mappedType.AssemblyQualifiedName,
                    true,
                    e.Event.Serialize(settings).AsByteArray(),
                    descriptor.Serialize(settings).AsByteArray()
                    );
            });

            try
            {
                await _client.AppendToStreamAsync(streamId, expectedVersion, translatedEvents);
            }
            catch (global::System.AggregateException e)
            {
                throw e.InnerException;
            }
        }
    }
}
