using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;

namespace Aggregates.Internal
{
    public class StoreEvents : IStoreEvents
    {
        private readonly ILogger Logger;
        private readonly StreamIdGenerator _generator;
        private readonly IEventStoreClient _client;
        private readonly IVersionRegistrar _registrar;
        public StoreEvents(ILogger<StoreEvents> logger, StreamIdGenerator generator, IVersionRegistrar registrar, IEventStoreClient client)
        {
            _generator = generator;
            _client = client;
            _registrar = registrar;

            Logger = logger;
        }

        public Task<IFullEvent[]> GetEvents<TEntity>(StreamDirection direction, string bucket, Id streamId, Id[] parents, long? start = null, int? count = null) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return _client.GetEvents(direction, stream, start, count);
        }

        public async Task<ISnapshot> GetSnapshot<TEntity>(string bucket, Id streamId, Id[] parents) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Snapshot, bucket, streamId, parents);
            IFullEvent @event;
            try
            {
                @event = (await _client.GetEvents(StreamDirection.Backwards, stream, count: 1).ConfigureAwait(false)).First();
            }
            catch (NotFoundException)
            {
                Logger.DebugEvent("NotFound", "Snapshot for [{Stream:l}] not found", stream);
                return null;
            }

            Logger.DebugEvent("Read", "Snapshot for [{Stream:l}] version {Version} found", stream, @event.Descriptor.Version);
            return new Snapshot
            {
                EntityType = @event.Descriptor.EntityType,
                Bucket = @event.Descriptor.Bucket,
                StreamId = @event.Descriptor.StreamId,
                Timestamp = @event.Descriptor.Timestamp,
                Version = @event.Descriptor.Version,
                Payload = @event.Event
            };
        }
        public Task WriteSnapshot<TEntity>(ISnapshot snapshot, IDictionary<string, string> commitHeaders) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Snapshot, snapshot.Bucket, snapshot.StreamId, snapshot.Parents?.Select(x => x.StreamId).ToArray());
            var e = new FullEvent
            {
                Descriptor = new EventDescriptor
                {
                    EntityType = snapshot.EntityType,
                    StreamType = StreamTypes.Snapshot,
                    Bucket = snapshot.Bucket,
                    StreamId = snapshot.StreamId,
                    Parents = snapshot.Parents,
                    Timestamp = DateTime.UtcNow,
                    Version = snapshot.Version,
                    Headers = new Dictionary<string, string>(),
                    CommitHeaders = commitHeaders
                },
                Event = snapshot.Payload as IState,
            };

            Logger.DebugEvent("Write", "Writing snapshot for [{Stream:l}] version {Version}", stream, snapshot.Version);
            return _client.WriteEvents(stream, new[] { e }, commitHeaders);
        }

        public Task<bool> VerifyVersion<TEntity>(string bucket, Id streamId, Id[] parents,
            long expectedVersion) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return _client.VerifyVersion(stream, expectedVersion);
        }

        public Task<long> WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, IDictionary<string, string> commitHeaders, long? expectedVersion = null) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return _client.WriteEvents(stream, events, commitHeaders, expectedVersion);
        }

        public Task WriteMetadata<TEntity>(string bucket, Id streamId, Id[] parents, int? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null,
            TimeSpan? cacheControl = null) where TEntity : IEntity
        {
            var stream = _generator(_registrar.GetVersionedName(typeof(TEntity)), StreamTypes.Domain, bucket, streamId, parents);
            return _client.WriteMetadata(stream, maxCount, truncateBefore, maxAge, cacheControl);
        }


    }
}
