using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates.Internal
{
    internal class StoreEvents : IStoreEvents
    {
        private static readonly Meter HitMeter = Metric.Meter("Stream Cache Hits", Unit.Events);
        private static readonly Meter MissMeter = Metric.Meter("Stream Cache Misses", Unit.Events);
        private static readonly Metrics.Timer ReadTime = Metric.Timer("EventStore Read Time", Unit.Events);
        private static readonly Metrics.Timer WriteTime = Metric.Timer("EventStore Write Time", Unit.Events);

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

        public Task Evict<T>(string bucket, string streamId) where T : class, IEventSource
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

        public async Task<IEventStream> GetStream<T>(string bucket, string streamId, ISnapshot snapshot = null) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            var events = new List<ResolvedEvent>();

            var sliceStart = snapshot?.Version + 1 ?? StreamPosition.Start;
            Logger.Write(LogLevel.Debug, () => $"Retreiving stream [{streamId}] in bucket [{bucket}] starting at {sliceStart}");
            
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

            while (await CheckFrozen<T>(bucket, streamId).ConfigureAwait(false))
            {
                Logger.Write(LogLevel.Debug, () => $"Stream [{streamName}] is frozen - waiting");
                Thread.Sleep(100);
            }


            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                Binder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };


            var readSize = _nsbSettings.Get<int>("ReadSize");
            StreamEventsSlice current;
            Logger.Write(LogLevel.Debug, () => $"Reading events for stream [{streamName}] starting at {sliceStart} from store");

            using (ReadTime.NewContext())
            {
                do
                {
                    current =
                        await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, readSize, false)
                            .ConfigureAwait(false);

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Retreived {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                    events.AddRange(current.Events);
                    sliceStart = current.NextEventNumber;
                } while (!current.IsEndOfStream);
            }
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
                await Cache<T>(eventstream).ConfigureAwait(false);

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

            using (ReadTime.NewContext())
            {
                do
                {
                    var take = Math.Min((count ?? int.MaxValue) - events.Count, readSize);

                    current =
                        await _client.ReadStreamEventsForwardAsync(streamName, sliceStart, take, false)
                            .ConfigureAwait(false);

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Retreived {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                    events.AddRange(current.Events);

                    sliceStart = current.NextEventNumber;
                } while (!current.IsEndOfStream);
            }

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

            using (ReadTime.NewContext())
            {
                do
                {
                    var take = Math.Min((count ?? int.MaxValue) - events.Count, readSize);

                    current =
                        await _client.ReadStreamEventsBackwardAsync(streamName, sliceStart, take, false)
                            .ConfigureAwait(false);

                    Logger.Write(LogLevel.Debug,
                        () =>
                                $"Retreived backwards {current.Events.Length} events from position {sliceStart}. Status: {current.Status} LastEventNumber: {current.LastEventNumber} NextEventNumber: {current.NextEventNumber}");

                    events.AddRange(current.Events);

                    sliceStart = current.NextEventNumber;
                } while (!current.IsEndOfStream);
            }
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

            using (WriteTime.NewContext())
            {
                await
                    _client.AppendToStreamAsync(streamName, ExpectedVersion.Any, translatedEvents).ConfigureAwait(false);
            }
        }

        public async Task WriteEvents<T>(string bucket, string streamId, int expectedVersion, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Writing {events.Count()} events to stream id [{streamName}].  Expected version: {expectedVersion}");

            if (await CheckFrozen<T>(bucket, streamId).ConfigureAwait(false))
                throw new FrozenException();

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

            using (WriteTime.NewContext())
            {
                await _client.AppendToStreamAsync(streamName, expectedVersion, translatedEvents).ConfigureAwait(false);
            }
        }


        public async Task WriteStreamMetadata<T>(string bucket, string streamId, int? maxCount = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Writing metadata [ {nameof(maxCount)}: {maxCount}, {nameof(maxAge)}: {maxAge}, {nameof(cacheControl)}: {cacheControl} ] to stream [{streamName}]");

            var existing = await _client.GetStreamMetadataAsync(streamName).ConfigureAwait(false);

            var metadata = StreamMetadata.Build();

            if ((maxCount ?? existing.StreamMetadata?.MaxCount).HasValue)
                metadata.SetMaxCount((maxCount ?? existing.StreamMetadata?.MaxCount).Value);
            if ((maxAge ?? existing.StreamMetadata?.MaxAge).HasValue)
                metadata.SetMaxAge((maxAge ?? existing.StreamMetadata?.MaxAge).Value);
            if ((cacheControl ?? existing.StreamMetadata?.CacheControl).HasValue)
                metadata.SetCacheControl((cacheControl ?? existing.StreamMetadata?.CacheControl).Value);

            await _client.SetStreamMetadataAsync(streamName, ExpectedVersion.Any, metadata).ConfigureAwait(false);
        }

        public async Task Freeze<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Freezing stream [{streamName}]");


            var existing = await _client.GetStreamMetadataAsync(streamName).ConfigureAwait(false);

            if ((existing.StreamMetadata?.CustomKeys.Contains("frozen") ?? false))
                throw new FrozenException();

            var metadata = StreamMetadata.Build();

            if ((existing.StreamMetadata?.MaxCount).HasValue)
                metadata.SetMaxCount((existing.StreamMetadata?.MaxCount).Value);
            if ((existing.StreamMetadata?.MaxAge).HasValue)
                metadata.SetMaxAge((existing.StreamMetadata?.MaxAge).Value);
            if ((existing.StreamMetadata?.CacheControl).HasValue)
                metadata.SetCacheControl((existing.StreamMetadata?.CacheControl).Value);

            metadata.SetCustomProperty("frozen", DateTime.UtcNow.ToUnixTime());
            metadata.SetCustomProperty("owner", Defaults.Instance.ToString());

            try
            {
                await
                    _client.SetStreamMetadataAsync(streamName, existing.MetastreamVersion, metadata)
                        .ConfigureAwait(false);
            }
            catch (WrongExpectedVersionException)
            {
                Logger.Write(LogLevel.Warn, () => $"Freeze: stream [{streamName}] someone froze before us");
                throw new FrozenException();
            }
        }

        public async Task Unfreeze<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            Logger.Write(LogLevel.Debug, () => $"Unfreezing stream [{streamName}]");

            var existing = await _client.GetStreamMetadataAsync(streamName).ConfigureAwait(false);

            if (existing.StreamMetadata == null || (existing.StreamMetadata?.CustomKeys.Contains("frozen") ?? false) == false || existing.StreamMetadata?.GetValue<string>("owner") != Defaults.Instance.ToString())
            {
                Logger.Write(LogLevel.Debug, () => $"Unfreeze: stream [{streamName}] is not frozen");
                return;
            }


            var metadata = StreamMetadata.Build();

            if ((existing.StreamMetadata?.MaxCount).HasValue)
                metadata.SetMaxCount((existing.StreamMetadata?.MaxCount).Value);
            if ((existing.StreamMetadata?.MaxAge).HasValue)
                metadata.SetMaxAge((existing.StreamMetadata?.MaxAge).Value);
            if ((existing.StreamMetadata?.CacheControl).HasValue)
                metadata.SetCacheControl((existing.StreamMetadata?.CacheControl).Value);


            try
            {
                await _client.SetStreamMetadataAsync(streamName, existing.MetastreamVersion, metadata).ConfigureAwait(false);
            }
            catch (WrongExpectedVersionException)
            {
                Logger.Write(LogLevel.Warn, () => $"Unfreeze: stream [{streamName}] metadata is inconsistent");
                throw new FrozenException();
            }
        }
        private async Task<bool> CheckFrozen<T>(string bucket, string streamId) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), bucket, streamId);
            var streamMeta = await _client.GetStreamMetadataAsync(streamName).ConfigureAwait(false);
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
                Logger.Warn($"Stream [{streamName}] has been frozen for over 60 seconds!  Unfreezing");
                await Unfreeze<T>(bucket, streamId).ConfigureAwait(false);
                return false;
            }
            return true;
        }

    }
}
