using Aggregates.Contracts;
using Aggregates.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Logging;

namespace Aggregates.Internal
{
    public class OobWriter : IOobWriter
    {
        private static readonly ILog Logger = LogProvider.GetLogger("OobWriter");

        private readonly IMessageDispatcher _dispatcher;
        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _generator;

        private static readonly ConcurrentDictionary<string, int> DaysToLiveKnowns = new ConcurrentDictionary<string, int>();

        public OobWriter(IMessageDispatcher dispatcher, IStoreEvents store)
        {
            _dispatcher = dispatcher;
            _store = store;
            _generator = Configuration.Settings.Generator;
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

        public async Task WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, Guid commitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity
        {
            Logger.DebugEvent("Write", "{Events} stream [{Stream:l}] bucket [{Bucket:l}]", events.Length, streamId, bucket);

            var transients = new List<IFullMessage>();
            var durables = new Dictionary<string, List<IFullEvent>>();

            foreach (var @event in events)
            {
                var parentsStr = parents?.Any() ?? false ? parents.Aggregate<Id, string>("", (cur, next) => $"{cur},{next}") : "";

                var headers = new Dictionary<string, string>()
                {
                    [$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"] = @event.EventId.ToString(),
                    [$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"] = commitId.ToString(),
                    [$"{Defaults.PrefixHeader}.EventId"] = @event.EventId.ToString(),
                    [$"{Defaults.PrefixHeader}.EntityType"] = @event.Descriptor.EntityType,
                    [$"{Defaults.PrefixHeader}.Timestamp"] = @event.Descriptor.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    [$"{Defaults.PrefixHeader}.Version"] = @event.Descriptor.Version.ToString(),
                    [$"{Defaults.PrefixHeader}.Bucket"] = bucket,
                    [$"{Defaults.PrefixHeader}.StreamId"] = streamId,
                    [$"{Defaults.PrefixHeader}.Parents"] = parentsStr
                };


                string id = "";

                id = @event.Descriptor.Headers[Defaults.OobHeaderKey];

                if (@event.Descriptor.Headers.ContainsKey(Defaults.OobTransientKey) &&
                    bool.TryParse(@event.Descriptor.Headers[Defaults.OobTransientKey], out var transient) && !transient)
                {
                    var stream = _generator(typeof(TEntity), StreamTypes.OOB, $"{id}.{bucket}", streamId, parents);
                    if (!durables.ContainsKey(stream))
                        durables[stream] = new List<IFullEvent>();

                    durables[stream].Add(new FullEvent
                    {
                        EventId = @event.EventId,
                        Event = @event.Event,
                        Descriptor = new EventDescriptor
                        {
                            EventId = @event.Descriptor.EventId,
                            EntityType = @event.Descriptor.EntityType,
                            StreamType = @event.Descriptor.StreamType,
                            Bucket = @event.Descriptor.Bucket,
                            StreamId = @event.Descriptor.StreamId,
                            Parents = @event.Descriptor.Parents,
                            Compressed = @event.Descriptor.Compressed,
                            Version = @event.Descriptor.Version,
                            Timestamp = @event.Descriptor.Timestamp,
                            Headers = @event.Descriptor.Headers.Merge(headers),
                            CommitHeaders = @event.Descriptor.CommitHeaders.Merge(commitHeaders)
                        }
                    });
                }
                else
                {
                    transients.Add(new FullMessage
                    {
                        Message = @event.Event,
                        Headers = @event.Descriptor.Headers.Merge(headers).Merge(commitHeaders)
                    });
                }
            }

            await _dispatcher.Publish(transients.ToArray()).ConfigureAwait(false);
            foreach (var stream in durables)
            {
                // Commit headers were already added to descriptor
                await _store.WriteEvents(stream.Key, stream.Value.ToArray(), new Dictionary<string, string> { }).ConfigureAwait(false);
                // Update stream's maxAge if oob channel has a DaysToLive parameter
                DaysToLiveKnowns.TryGetValue(stream.Key, out var daysToLiveKnown);

                var sample = stream.Value.FirstOrDefault(x => x.Descriptor.Headers.ContainsKey(Defaults.OobDaysToLiveKey));
                if (sample == null)
                    continue;

                int.TryParse(sample.Descriptor.Headers[Defaults.OobDaysToLiveKey], out var daysToLive);

                if (daysToLiveKnown != daysToLive)
                {
                    DaysToLiveKnowns.AddOrUpdate(stream.Key, daysToLive, (key, val) => daysToLive);
                    await _store.WriteMetadata(stream.Key, maxAge: TimeSpan.FromDays(daysToLive)).ConfigureAwait(false);
                }

            }

        }
    }

}
