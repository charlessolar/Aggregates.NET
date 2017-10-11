using Aggregates.Contracts;
using Aggregates.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class OobWriter : IOobWriter
    {
        private readonly IMessageDispatcher _dispatcher;
        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _generator;

        private static ConcurrentDictionary<string, int> DaysToLiveKnowns = new ConcurrentDictionary<string, int>();

        public OobWriter(IMessageDispatcher dispatcher, IStoreEvents store, StreamIdGenerator generator)
        {
            _dispatcher = dispatcher;
            _store = store;
            _generator = generator;
        }

        public Task<long> GetSize<TEntity>(string bucket, Id streamId, Id[] parents, string oobId) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.OOB, $"{oobId}.{bucket}", streamId, parents);
            return _store.Size(stream);
        }
        public Task<IFullEvent[]> GetEvents<TEntity>(string bucket, Id streamId, Id[] parents, string oobId, long? start = null, int? count = null) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.OOB, $"{oobId}.{bucket}", streamId, parents);
            return _store.GetEvents(stream, start, count);
        }
        public Task<IFullEvent[]> GetEventsBackwards<TEntity>(string bucket, Id streamId, Id[] parents, string oobId, long? start = null, int? count = null) where TEntity : IEntity
        {
            var stream = _generator(typeof(TEntity), StreamTypes.OOB, $"{oobId}.{bucket}", streamId, parents);
            return _store.GetEventsBackwards(stream, start, count);
        }

        public async Task WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, IDictionary<string, string> commitHeaders) where TEntity : IEntity
        {
            await events.WhenAllAsync(async @event =>
            {
                var message = new FullMessage
                {
                    Message = @event.Event,
                    Headers = @event.Descriptor.Headers
                };

                var parentsStr = parents?.Any() ?? false ? parents.Aggregate<Id, string>("", (cur, next) => $"{cur},{next}") : "";

                var headers = new Dictionary<string, string>()
                {
                    [$"{Defaults.PrefixHeader}.EventId"] = @event.EventId.ToString(),
                    [$"{Defaults.PrefixHeader}.EntityType"] = @event.Descriptor.EntityType,
                    [$"{Defaults.PrefixHeader}.Timestamp"] = @event.Descriptor.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    [$"{Defaults.PrefixHeader}.Version"] = @event.Descriptor.Version.ToString(),
                    [$"{Defaults.PrefixHeader}.Bucket"] = bucket,
                    [$"{Defaults.PrefixHeader}.StreamId"] = streamId,
                    [$"{Defaults.PrefixHeader}.Parents"] = parentsStr
                };

                string id = "";
                bool transient = true;
                int daysToLive = -1;

                id = @event.Descriptor.Headers[Defaults.OobHeaderKey];

                if (@event.Descriptor.Headers.ContainsKey(Defaults.OobTransientKey))
                    bool.TryParse(@event.Descriptor.Headers[Defaults.OobTransientKey], out transient);
                if (@event.Descriptor.Headers.ContainsKey(Defaults.OobDaysToLiveKey))
                    int.TryParse(@event.Descriptor.Headers[Defaults.OobDaysToLiveKey], out daysToLive);

                if (!transient)
                {
                    var stream = _generator(typeof(TEntity), StreamTypes.OOB, $"{id}.{bucket}", streamId, parents);
                    var version = await _store.WriteEvents(stream, new[] { @event }, headers).ConfigureAwait(false);
                    if (daysToLive != -1)
                    {
                        var key = $"{bucket}.{id}.{streamId}.{parentsStr}";
                        // Uses the dictionary to keep track of daysToLive data its already saved.
                        // If an entity saves the same stream with a new daysToLive the stream metadata needs to be rewritten
                        if (!DaysToLiveKnowns.ContainsKey(key) || DaysToLiveKnowns[key] != daysToLive)
                        {
                            DaysToLiveKnowns[key] = daysToLive;
                            await _store.WriteMetadata(stream, maxAge: TimeSpan.FromDays(daysToLive)).ConfigureAwait(false);
                        }
                    }
                }
                else
                    await _dispatcher.Publish(message, headers).ConfigureAwait(false);

            }).ConfigureAwait(false);

        }
    }
    
}
