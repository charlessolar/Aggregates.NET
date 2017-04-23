using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class DefaultOobHandler : IOobHandler
    {
        private readonly IStoreEvents _store;
        private readonly StreamIdGenerator _streamGen;

        public DefaultOobHandler(IStoreEvents store, StreamIdGenerator streamGen)
        {
            _store = store;
            _streamGen = streamGen;
        }


        public async Task Publish<T>(string bucket, Id streamId, IEnumerable<Id> parents, IEnumerable<IFullEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.OOB, bucket, streamId, parents);
            var writableEvents = events as IFullEvent[] ?? events.ToArray();
            if(await _store.WriteEvents(streamName, writableEvents, commitHeaders).ConfigureAwait(false) == (writableEvents.Count() - 1))
                // New stream - write metadata
                await _store.WriteMetadata(streamName, maxCount: 200000).ConfigureAwait(false);
            
        }
        public Task<IEnumerable<IFullEvent>> Retrieve<T>(string bucket, Id streamId, IEnumerable<Id> parents, long? skip = null, int? take = null, bool ascending = true) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.OOB, bucket, streamId, parents);
            return !ascending ? _store.GetEventsBackwards(streamName, skip, take) : _store.GetEvents(streamName, skip, take);
        }

        public Task<long> Size<T>(string bucket, Id streamId, IEnumerable<Id> parents) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.OOB, bucket, streamId, parents);
            return _store.Size(streamName);
        }
    }
}
