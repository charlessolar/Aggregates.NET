using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Extensions;

namespace Aggregates.Internal
{
    class DefaultOobHandler : IOobHandler
    {
        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _streamGen;
        private readonly Random _random;

        public DefaultOobHandler(IStoreEvents store, StreamIdGenerator streamGen)
        {
            _store = store;
            _streamGen = streamGen;
            _random = new Random();
        }


        public async Task Publish<T>(string bucket, Id streamId, IEnumerable<Id> parents, IEnumerable<IFullEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            // OOB events of the same stream name don't need to all be written to the same stream
            // if we parallelize the events into 10 known streams we can take advantage of internal
            // ES optimizations and ES sharding
            var vary = _random.Next(10) + 1;

            var streamName = _streamGen(typeof(T), StreamTypes.OOB, bucket, streamId, parents) + $".{vary}";
            var writableEvents = events as IFullEvent[] ?? events.ToArray();
            await _store.WriteEvents(streamName, writableEvents, commitHeaders).ConfigureAwait(false);
        }
        public async Task<IEnumerable<IFullEvent>> Retrieve<T>(string bucket, Id streamId, IEnumerable<Id> parents, long skip, int take, bool ascending = true) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.OOB, bucket, streamId, parents);

            take = take / 10;
            // if take is 100, take 10 from each of 10 streams - see above
            var events = await Enumerable.Range(1, 10).ToArray()
                .StartEachAsync(5,
                    (vary) => !@ascending
                        ? _store.GetEventsBackwards(streamName + $"{vary}", skip, take)
                        : _store.GetEvents(streamName + $"{vary}", skip, take))
                .ConfigureAwait(false);
            return events.SelectMany(x => x);
        }

        public async Task<long> Size<T>(string bucket, Id streamId, IEnumerable<Id> parents) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.OOB, bucket, streamId, parents);
            return (await Enumerable.Range(1, 10).ToArray().StartEachAsync(5, (vary) => _store.Size(streamName))).Sum();
        }
    }
}
