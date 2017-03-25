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
        private static readonly Meter FrozenExceptions = Metric.Meter("Frozen Exceptions", Unit.Items, tags: "debug");
        private static readonly Histogram WrittenEvents = Metric.Histogram("EventStore Written", Unit.Events);
        private static readonly Histogram ReadEvents = Metric.Histogram("EventStore Read", Unit.Events);
        private static readonly Histogram WrittenEventsSize = Metric.Histogram("EventStore Written Size", Unit.Bytes);
        private static readonly Histogram ReadEventsSize = Metric.Histogram("EventStore Read Size", Unit.Bytes);
        private static readonly Metrics.Timer ReadTime = Metric.Timer("EventStore Read Time", Unit.Items);
        private static readonly Metrics.Timer WriteTime = Metric.Timer("EventStore Write Time", Unit.Items);

        private static readonly ILog Logger = LogManager.GetLogger("StoreEvents");
        private static readonly ILog SlowLogger = LogManager.GetLogger("Slow Alarm");
        private readonly IEventStoreConnection[] _clients;
        private readonly IMessageMapper _mapper;
        private readonly int _readsize;
        private readonly Compression _compress;

        public StoreEvents(IMessageMapper mapper, int readsize, Compression compress, IEventStoreConnection[] connections)
        {
            _clients = connections;
            _mapper = mapper;
            _readsize = readsize;
            _compress = compress;
        }


        public async Task<IEnumerable<IWritableEvent>> GetEvents(string stream, long? start = null, int? count = null)
        {

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            var bucket = Math.Abs(stream.GetHashCode() % _clients.Count());

            var sliceStart = start ?? StreamPosition.Start;
            StreamEventsSlice current;
            Logger.Write(LogLevel.Debug, () => $"Reading events from stream [{stream}] starting at {sliceStart}");

            var events = new List<ResolvedEvent>();
            using (var ctx = ReadTime.NewContext())
            {
                do
                {
                    var readsize = _readsize;
                    if (count.HasValue)
                        readsize = Math.Min(count.Value - events.Count, _readsize);

                    current =
                        await _clients[bucket].ReadStreamEventsForwardAsync(stream, sliceStart, readsize, false)
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
                Logger.Write(LogLevel.Warn, () => $"Stream [{stream}] does not exist!");
                throw new NotFoundException($"Stream [{stream}] does not exist!");
            }

            ReadEvents.Update(events.Count);
            ReadEventsSize.Update(events.Sum(x => x.Event.Data.Length));
            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;

                var descriptor = metadata.Deserialize(settings);

                if (descriptor.Compressed)
                    data = data.Decompress();

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

            Logger.Write(LogLevel.Info, () => $"Read {translatedEvents.Count} events from stream [{stream}]");
            return translatedEvents;
        }


        public async Task<IEnumerable<IWritableEvent>> GetEventsBackwards(string stream, long? start = null, int? count = null)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new EventSerializationBinder(_mapper),
                ContractResolver = new EventContractResolver(_mapper)
            };

            var bucket = Math.Abs(stream.GetHashCode() % _clients.Count());

            var events = new List<ResolvedEvent>();
            var sliceStart = StreamPosition.End;

            StreamEventsSlice current;
            Logger.Write(LogLevel.Debug, () => $"Reading events from stream [{stream}] starting at {sliceStart}");
            if (start.HasValue || count == 1)
            {
                // Interesting, ReadStreamEventsBackwardAsync's [start] parameter marks start from begining of stream, not an offset from the end.
                // Read 1 event from the end, to figure out where start should be
                var result = await _clients[bucket].ReadStreamEventsBackwardAsync(stream, StreamPosition.End, 1, false).ConfigureAwait(false);
                sliceStart = result.NextEventNumber - start ?? 0;

                // Special case if only 1 event is requested no reason to read any more
                if (count == 1)
                    events.AddRange(result.Events);
            }

            if (!count.HasValue || count > 1)
            {
                Logger.Write(LogLevel.Debug,
                    () => $"Reading events backwards from stream [{stream}] starting at {sliceStart}");

                using (var ctx = ReadTime.NewContext())
                {
                    do
                    {
                        var take = Math.Min((count ?? int.MaxValue) - events.Count, _readsize);

                        current =
                            await _clients[bucket].ReadStreamEventsBackwardAsync(stream, sliceStart, take, false)
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
                    Logger.Write(LogLevel.Warn, () => $"Stream [{stream}] does not exist!");
                    throw new NotFoundException($"Stream [{stream}] does not exist!");
                }
            }

            ReadEvents.Update(events.Count);
            ReadEventsSize.Update(events.Sum(x => x.Event.Data.Length));
            var translatedEvents = events.Select(e =>
            {
                var metadata = e.Event.Metadata;
                var data = e.Event.Data;

                var descriptor = metadata.Deserialize(settings);

                if (descriptor.Compressed)
                    data = data.Decompress();

                var @event = data.Deserialize(e.Event.EventType, settings);

                // Special case if event was written without a version - substitute the position from store
                if (descriptor.Version == 0)
                    descriptor.Version = e.Event.EventNumber;

                return new WritableEvent
                {
                    Descriptor = descriptor,
                    Event = @event,
                    EventId = e.Event.EventId
                };
            }).ToList();

            Logger.Write(LogLevel.Info, () => $"Read {translatedEvents.Count} events backwards from stream [{stream}]");
            return translatedEvents;
        }

        public Task<long> WriteSnapshot(string stream, IWritableEvent snapshot,
            IDictionary<string, string> commitHeaders)
        {
            Logger.Write(LogLevel.Debug, () => $"Writing snapshot to stream id [{stream}]");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new EventSerializationBinder(_mapper),
                //ContractResolver = new EventContractResolver(_mapper)
            };

            var descriptor = snapshot.Descriptor;
            descriptor.EventId = snapshot.EventId ?? Guid.NewGuid();
            descriptor.CommitHeaders = commitHeaders ?? new Dictionary<string, string>();

            var mappedType = snapshot.Event.GetType();
            if (!mappedType.IsInterface)
                mappedType = _mapper.GetMappedTypeFor(mappedType) ?? mappedType;

            var @event = snapshot.Event.Serialize(settings).AsByteArray();

            if (_compress.HasFlag(Compression.Snapshots))
            {
                descriptor.Compressed = true;
                @event = @event.Compress();
            }
            var metadata = descriptor.Serialize(settings).AsByteArray();

            var data = new EventData(
                descriptor.EventId,
                mappedType.AssemblyQualifiedName,
                !descriptor.Compressed,
                @event,
                metadata
                );
            return DoWrite(stream, new[] { data });
        }

        public Task<long> WriteEvents(string stream, IEnumerable<IWritableEvent> events,
            IDictionary<string, string> commitHeaders, long? expectedVersion = null)
        {
            Logger.Write(LogLevel.Info, () => $"Writing {events.Count()} events to stream id [{stream}].  Expected version: {expectedVersion}");

            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new EventSerializationBinder(_mapper),
                //ContractResolver = new EventContractResolver(_mapper)
            };

            var translatedEvents = events.Select(e =>
            {
                var descriptor = e.Descriptor;
                descriptor.EventId = e.EventId ?? Guid.NewGuid();
                descriptor.CommitHeaders = commitHeaders ?? new Dictionary<string,string>();

                var mappedType = e.Event.GetType();
                if (!mappedType.IsInterface)
                    mappedType = _mapper.GetMappedTypeFor(mappedType) ?? mappedType;

                var @event = e.Event.Serialize(settings).AsByteArray();
                if (_compress.HasFlag(Compression.Events))
                {
                    descriptor.Compressed = true;
                    @event = @event.Compress();
                }
                var metadata = descriptor.Serialize(settings).AsByteArray();


                return new EventData(
                    descriptor.EventId,
                    mappedType.AssemblyQualifiedName,
                    !descriptor.Compressed,
                    @event,
                    metadata
                    );
            }).ToList();

            return DoWrite(stream, translatedEvents, expectedVersion);
        }

        private async Task<long> DoWrite(string stream, IEnumerable<EventData> events, long? expectedVersion = null)
        {

            var bucket = Math.Abs(stream.GetHashCode() % _clients.Count());

            long nextVersion;
            using (var ctx = WriteTime.NewContext())
            {
                EventStoreTransaction transaction = null;
                try
                {
                    if (events.Count() > _readsize)
                        transaction = await _clients[bucket].StartTransactionAsync(stream, expectedVersion ?? ExpectedVersion.Any).ConfigureAwait(false);

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
                            _clients[bucket].AppendToStreamAsync(stream, expectedVersion ?? ExpectedVersion.Any,
                                    events)
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

                WrittenEvents.Update(events.Count());
                WrittenEventsSize.Update(events.Sum(x => x.Data.Length));
                if (ctx.Elapsed > TimeSpan.FromSeconds(1))
                    SlowLogger.Write(LogLevel.Warn, () => $"Writing {events.Count()} events of total size {events.Sum(x => x.Data.Length)} to stream [{stream}] version {expectedVersion} took {ctx.Elapsed.TotalSeconds} seconds!");
                Logger.Write(LogLevel.Info, () => $"Writing {events.Count()} events of total size {events.Sum(x => x.Data.Length)} to stream [{stream}] version {expectedVersion} took {ctx.Elapsed.TotalMilliseconds} ms");
            }
            return nextVersion;
        }

        public async Task WriteMetadata(string stream, long? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null,
            TimeSpan? cacheControl = null, bool? frozen = null, Guid? owner = null, bool force = false, IDictionary<string, string> custom=null)
        {
            var bucket = Math.Abs(stream.GetHashCode() % _clients.Count());
            Logger.Write(LogLevel.Debug, () => $"Writing metadata to stream [{stream}] [ {nameof(maxCount)}: {maxCount}, {nameof(maxAge)}: {maxAge}, {nameof(cacheControl)}: {cacheControl}, {nameof(frozen)}: {frozen} ]");

            var existing = await _clients[bucket].GetStreamMetadataAsync(stream).ConfigureAwait(false);

            try
            {
                if ((existing.StreamMetadata?.CustomKeys.Contains("frozen") ?? false) &&
                    existing.StreamMetadata?.GetValue<string>("owner") != Defaults.Instance.ToString())
                {
                    FrozenExceptions.Mark();
                    throw new VersionException("Stream is frozen - we are not the owner");
                }
                if (frozen.HasValue && !force && frozen == false && (
                        existing.StreamMetadata == null ||
                        (existing.StreamMetadata?.CustomKeys.Contains("frozen") ?? false) == false ||
                        existing.StreamMetadata?.GetValue<string>("owner") != Defaults.Instance.ToString()))
                {
                    FrozenExceptions.Mark();
                    throw new FrozenException();
                }

                // If we are trying to freeze the stream that we've already frozen (to prevent multiple threads from attempting to process the same frozen data)
                if (frozen.HasValue && frozen == true &&
                    (existing.StreamMetadata?.CustomKeys.Contains("frozen") ?? false) &&
                    existing.StreamMetadata?.GetValue<string>("owner") == Defaults.Instance.ToString())
                {
                    FrozenExceptions.Mark();
                    throw new FrozenException();
                }
            }
            catch (FrozenException)
            {

                var time = existing.StreamMetadata.GetValue<long>("frozen");
                if ((DateTime.UtcNow.ToUnixTime() - time) > 60)
                    SlowLogger.Write(LogLevel.Warn, () => $"Stream [{stream}] has been frozen for {DateTime.UtcNow.ToUnixTime() - time} seconds!");
                throw;
            }

            var metadata = StreamMetadata.Build();

            if ((maxCount ?? existing.StreamMetadata?.MaxCount).HasValue)
                metadata.SetMaxCount((maxCount ?? existing.StreamMetadata?.MaxCount).Value);
            if ((truncateBefore ?? existing.StreamMetadata?.TruncateBefore).HasValue)
                metadata.SetTruncateBefore(Math.Max(truncateBefore ?? 0, (truncateBefore ?? existing.StreamMetadata?.TruncateBefore).Value));
            if ((maxAge ?? existing.StreamMetadata?.MaxAge).HasValue)
                metadata.SetMaxAge((maxAge ?? existing.StreamMetadata?.MaxAge).Value);
            if ((cacheControl ?? existing.StreamMetadata?.CacheControl).HasValue)
                metadata.SetCacheControl((cacheControl ?? existing.StreamMetadata?.CacheControl).Value);

            if (frozen.HasValue && frozen == true)
                metadata.SetCustomProperty("frozen", DateTime.UtcNow.ToUnixTime());
            if (owner.HasValue)
                metadata.SetCustomProperty("owner", Defaults.Instance.ToString());

            if (custom != null)
            {
                foreach (var kv in custom)
                    metadata.SetCustomProperty(kv.Key, kv.Value.ToString());
            }

            try
            {
                Logger.Write(LogLevel.Debug, () => $"Writing metadata to stream [{stream}] version {existing.MetastreamVersion} ");
                await _clients[bucket].SetStreamMetadataAsync(stream, existing.MetastreamVersion, metadata).ConfigureAwait(false);

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

        public async Task<string> GetMetadata(string stream, string key)
        {
            var bucket = Math.Abs(stream.GetHashCode() % _clients.Count());
            Logger.Write(LogLevel.Debug, () => $"Getting metadata '{key}' from stream [{stream}]");

            var existing = await _clients[bucket].GetStreamMetadataAsync(stream).ConfigureAwait(false);
            
            if ((existing.StreamMetadata?.CustomKeys.Contains("frozen") ?? false))
            {
                FrozenExceptions.Mark();
                throw new FrozenException();
            }
            if (existing.StreamMetadata == null)
                Logger.Write(LogLevel.Debug, () => $"No metadata exists for stream [{stream}]");

            string property = "";
            if (!existing.StreamMetadata?.TryGetValue(key, out property) ?? false)
                property = "";
            return property;
        }

        public async Task<bool> IsFrozen(string stream)
        {
            var bucket = Math.Abs(stream.GetHashCode() % _clients.Count());

            var streamMeta = await _clients[bucket].GetStreamMetadataAsync(stream).ConfigureAwait(false);
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
                await WriteMetadata(stream, force: true).ConfigureAwait(false);
                return false;
            }
            return true;
        }
    }
}
