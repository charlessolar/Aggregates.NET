using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    public class DefaultOobHandler : IOobHandler
    {
        private readonly IStoreEvents _store;

        public DefaultOobHandler(IStoreEvents store)
        {
            _store = store;
        }


        public async Task Publish<T>(string bucket, string streamId, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource
        {
            var writableEvents = events as IWritableEvent[] ?? events.ToArray();
            await _store.AppendEvents<T>(bucket + ".OOB", streamId, writableEvents, commitHeaders).ConfigureAwait(false);

            // if the stream is new, also write stream metadata to limit the number of events stored
            // OOB events are by definition not mission critical so we can save a bit of space by scavaging old data
            if (writableEvents.Any(x => x.Descriptor.Version == 0))
                await _store.WriteStreamMetadata<T>(bucket + ".OOB", streamId, maxCount: 200000).ConfigureAwait(false);
        }
        public Task<IEnumerable<IWritableEvent>> Retrieve<T>(string bucket, string streamId, int? skip = null, int? take = null, bool ascending = true) where T : class, IEventSource
        {
            return !ascending ? _store.GetEventsBackwards<T>(bucket + ".OOB", streamId) : _store.GetEvents<T>(bucket + ".OOB", streamId);
        }
    }
}
