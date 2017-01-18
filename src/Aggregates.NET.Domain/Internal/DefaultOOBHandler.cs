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


        public async Task Publish<T>(string bucket, string streamId, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.OOB, bucket, streamId);
            var writableEvents = events as IWritableEvent[] ?? events.ToArray();
            if(await _store.WriteEvents(streamName, writableEvents, commitHeaders).ConfigureAwait(false) == (writableEvents.Count() - 1))
                // New stream - write metadata
                await _store.WriteMetadata(streamName, maxCount: 200000).ConfigureAwait(false);
            
        }
        public Task<IEnumerable<IWritableEvent>> Retrieve<T>(string bucket, string streamId, int? skip = null, int? take = null, bool ascending = true) where T : class, IEventSource
        {
            var streamName = _streamGen(typeof(T), StreamTypes.OOB, bucket, streamId);
            return !ascending ? _store.GetEventsBackwards(streamName) : _store.GetEvents(streamName);
        }
    }
}
