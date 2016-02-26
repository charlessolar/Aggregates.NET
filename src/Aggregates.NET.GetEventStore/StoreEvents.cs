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
        private readonly JsonSerializerSettings _settings;

        public StoreEvents(IEventStoreConnection client, IBuilder builder, IMessageMapper mapper, IStoreSnapshots snapshots, JsonSerializerSettings settings)
        {
            _client = client;
            _mapper = mapper;
            _snapshots = snapshots;
            _settings = settings;
            _builder = builder;
        }


        public IEventStream GetStream<T>(String bucket, String stream, Int32? start = null) where T : class, IEntity
        {
            Logger.DebugFormat("Getting stream for stream '{0}' in bucket '{1}'", stream, bucket);

            var streamId = String.Format("{0}.{1}", bucket, stream);
            var events = new List<ResolvedEvent>();

            StreamEventsSlice current;
            var sliceStart = start ?? StreamPosition.Start;
            do
            {
                current = _client.ReadStreamEventsForwardAsync(streamId, sliceStart, 200, false).WaitForResult();

                events.AddRange(current.Events);
                sliceStart = current.NextEventNumber;
            } while (!current.IsEndOfStream);

            var translatedEvents = events.Select(e =>
            {
                var descriptor = e.Event.Metadata.Deserialize(_settings);
                var data = e.Event.Data.Deserialize(e.Event.EventType, _settings);

                return new Internal.WritableEvent
                {
                    Descriptor = descriptor,
                    Event = data,
                    EventId = e.Event.EventId
                };
            });
            
            return new Internal.EventStream<T>(_builder, this, _snapshots, bucket, stream, current.LastEventNumber, translatedEvents);
        }

        public void WriteEvents(String bucket, String stream, Int32 expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<String, String> commitHeaders)
        {
            Logger.DebugFormat("Writing {0} events to stream id '{1}'.  Expected version: {2}", events.Count(), stream, expectedVersion);
            var streamId = String.Format("{0}.{1}", bucket, stream);

            var translatedEvents = events.Select(e =>
            {
                var descriptor = new EventDescriptor
                {
                    EntityType = e.Descriptor.EntityType,
                    Timestamp = e.Descriptor.Timestamp,
                    Version = e.Descriptor.Version,
                    Headers = e.Descriptor.Headers.Merge(commitHeaders)
                };
                
                return new EventData(
                    e.EventId,
                    _mapper.GetMappedTypeFor(e.Event.GetType()).AssemblyQualifiedName,
                    true,
                    e.Event.Serialize(_settings).AsByteArray(),
                    descriptor.Serialize(_settings).AsByteArray()
                    );
            });

            try
            {
                _client.AppendToStreamAsync(streamId, expectedVersion, translatedEvents).Wait();
            }
            catch (global::System.AggregateException e)
            {
                throw e.InnerException;
            }
        }
    }
}
