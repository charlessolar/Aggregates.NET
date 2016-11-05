using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Metrics;
using Metrics.Utils;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    class StoreEvents : IStoreEvents
    {
        private static readonly Metrics.Timer ReadTime = Metric.Timer("EventStore Read Time", Unit.Events);
        private static readonly Metrics.Timer WriteTime = Metric.Timer("EventStore Write Time", Unit.Events);

        private static readonly ILog Logger = LogManager.GetLogger(typeof(StoreEvents));
        private readonly IEventStoreConnection _client;
        private readonly IMessageMapper _mapper;
        private readonly int _readsize;
        private readonly bool _compress;

        public StoreEvents(IEventStoreConnection client, IMessageMapper mapper, int readsize, bool compress)
        {
            _client = client;
            _mapper = mapper;
            _readsize = readsize;
            _compress = compress;
        }

        public async Task<IEnumerable<IWritableEvent>> GetEvents(string stream, int? start = null, int? count = null)
        {

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            var sliceStart = start ?? StreamPosition.Start;
            StreamEventsSlice current;
            Logger.Write(LogLevel.Debug, () => $"Reading events from stream [{stream}] starting at {sliceStart}");

            var events = new List<ResolvedEvent>();
            using (ReadTime.NewContext())
            {
                do
                {
                    current =
                        await _client.ReadStreamEventsForwardAsync(stream, sliceStart, _readsize, false)
                            .ConfigureAwait(false);

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Retreived {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                    events.AddRange(current.Events);
                    sliceStart = current.NextEventNumber;
                } while (!current.IsEndOfStream);
            }
            Logger.Write(LogLevel.Debug, () => $"Finished reading events from stream [{stream}]");

            if (current.Status == SliceReadStatus.StreamNotFound)
            {
                Logger.Write(LogLevel.Warn, () => $"Stream [{stream}] does not exist!");
                return null;
            }

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;

                // Assume compressed if not JSON
                if (!e.Event.IsJson)
                {
                    metadata = metadata.Decompress();
                    data = data.Decompress();
                }

                var descriptor = metadata.Deserialize(settings);
                var @event = data.Deserialize(e.Event.EventType, settings);

                // Special case if event was written without a version - substitue the position from store
                if (descriptor.Version == 0)
                    descriptor.Version = e.Event.EventNumber;

                return new WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            }).ToList();

            return translatedEvents;
        }


        public async Task<IEnumerable<IWritableEvent>> GetEventsBackwards(string stream, int? start = null, int? count = null)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
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
                var result = await _client.ReadStreamEventsBackwardAsync(stream, StreamPosition.End, 1, false).ConfigureAwait(false);
                sliceStart = result.NextEventNumber - start.Value;

                // Special case if only 1 event is requested no reason to read any more
                if (count == 1)
                    events.AddRange(result.Events);
            }

            if (count > 1)
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Reading events backwards from stream [{stream}] starting at {sliceStart}");

                using (ReadTime.NewContext())
                {
                    do
                    {
                        var take = Math.Min((count ?? int.MaxValue) - events.Count, _readsize);

                        current =
                            await _client.ReadStreamEventsBackwardAsync(stream, sliceStart, take, false)
                                .ConfigureAwait(false);

                        Logger.Write(LogLevel.Debug,
                            () =>
                                    $"Retreived backwards {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                        events.AddRange(current.Events);

                        sliceStart = current.NextEventNumber;
                    } while (!current.IsEndOfStream);
                }
                Logger.Write(LogLevel.Debug, () => $"Finished reading all events backward from stream [{stream}]");
            }

            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;
                // Assume compressed if not JSON
                if (!e.Event.IsJson)
                {
                    metadata = metadata.Decompress();
                    data = data.Decompress();
                }

                var descriptor = metadata.Deserialize(settings);
                var @event = data.Deserialize(e.Event.EventType, settings);

                // Special case if event was written without a version - substitue the position from store
                if (descriptor.Version == 0)
                    descriptor.Version = e.Event.EventNumber;

                return new WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            }).ToList();

            return translatedEvents;
        }

        public async Task<int> WriteEvents(string stream, IEnumerable<IWritableEvent> events,
            IDictionary<string, string> commitHeaders, int? expectedVersion = null)
        {

            Logger.Write(LogLevel.Debug, () => $"Writing {events.Count()} events to stream id [{stream}].  Expected version: {expectedVersion}");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Binder = new EventSerializationBinder(_mapper),
                //ContractResolver = new EventContractResolver(_mapper)
            };

            var translatedEvents = events.Select(e =>
            {
                var descriptor = new EventDescriptor
                {
                    EntityType = e.Descriptor.EntityType,
                    Timestamp = e.Descriptor.Timestamp,
                    Version = e.Descriptor.Version,
                    Headers = commitHeaders != null ? commitHeaders.Merge(e.Descriptor.Headers) : e.Descriptor.Headers
                };

                var mappedType = e.Event.GetType();
                if (!mappedType.IsInterface)
                    mappedType = _mapper.GetMappedTypeFor(mappedType) ?? mappedType;


                var @event = e.Event.Serialize(settings).AsByteArray();
                var metadata = descriptor.Serialize(settings).AsByteArray();
                if (_compress)
                {
                    @event = @event.Compress();
                    metadata = metadata.Compress();
                }

                return new EventData(
                    e.EventId ?? Guid.NewGuid(),
                    mappedType.AssemblyQualifiedName,
                    !_compress,
                    @event,
                    metadata
                    );
            }).ToList();

            using (WriteTime.NewContext())
            {
                try
                {
                    var result = await
                        _client.AppendToStreamAsync(stream, expectedVersion ?? ExpectedVersion.Any, translatedEvents)
                            .ConfigureAwait(false);
                    return result.NextExpectedVersion;
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
        }

        public async Task WriteMetadata(string stream, int? maxCount = null, int? truncateBefore = null, TimeSpan? maxAge = null,
            TimeSpan? cacheControl = null, bool? frozen = null, Guid? owner = null)
        {

            Logger.Write(LogLevel.Debug, () => $"Writing metadata [ {nameof(maxCount)}: {maxCount}, {nameof(maxAge)}: {maxAge}, {nameof(cacheControl)}: {cacheControl} ] to stream [{stream}]");

            var existing = await _client.GetStreamMetadataAsync(stream).ConfigureAwait(false);

            if ((existing.StreamMetadata?.CustomKeys.Contains("frozen") ?? false) && existing.StreamMetadata?.GetValue<string>("owner") != Defaults.Instance.ToString())
                throw new VersionException("Stream is frozen - we are not the owner");

            if (frozen.HasValue && frozen == false && (
                existing.StreamMetadata == null ||
                (existing.StreamMetadata?.CustomKeys.Contains("frozen") ?? false) == false ||
                existing.StreamMetadata?.GetValue<string>("owner") != Defaults.Instance.ToString()))
                throw new FrozenException();

            var metadata = StreamMetadata.Build();

            if ((maxCount ?? existing.StreamMetadata?.MaxCount).HasValue)
                metadata.SetMaxCount((maxCount ?? existing.StreamMetadata?.MaxCount).Value);
            if ((truncateBefore ?? existing.StreamMetadata?.TruncateBefore).HasValue)
                metadata.SetTruncateBefore((truncateBefore ?? existing.StreamMetadata?.TruncateBefore).Value);
            if ((maxAge ?? existing.StreamMetadata?.MaxAge).HasValue)
                metadata.SetMaxAge((maxAge ?? existing.StreamMetadata?.MaxAge).Value);
            if ((cacheControl ?? existing.StreamMetadata?.CacheControl).HasValue)
                metadata.SetCacheControl((cacheControl ?? existing.StreamMetadata?.CacheControl).Value);

            if (frozen.HasValue && frozen == true)
                metadata.SetCustomProperty("frozen", DateTime.UtcNow.ToUnixTime());
            if (owner.HasValue)
                metadata.SetCustomProperty("owner", Defaults.Instance.ToString());

            try
            {
                await _client.SetStreamMetadataAsync(stream, existing.MetastreamVersion, metadata).ConfigureAwait(false);

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

        public async Task<bool> IsFrozen(string stream)
        {

            var streamMeta = await _client.GetStreamMetadataAsync(stream).ConfigureAwait(false);
            if (!(streamMeta.StreamMetadata?.CustomKeys.Contains("frozen") ?? false))
                return false;

            // ReSharper disable once PossibleNullReferenceException
            var owner = streamMeta.StreamMetadata.GetValue<string>("owner");
            if (owner == Defaults.Instance.ToString())
                return false;

            // ReSharper disable once PossibleNullReferenceException
            var time = streamMeta.StreamMetadata.GetValue<long>("frozen");
            if ((DateTime.UtcNow.ToUnixTime() - time) > 60)
            {
                Logger.Warn($"Stream [{stream}] has been frozen for over 60 seconds!  Unfreezing");
                await WriteMetadata(stream).ConfigureAwait(false);
                return false;
            }
            return true;
        }
    }
}
