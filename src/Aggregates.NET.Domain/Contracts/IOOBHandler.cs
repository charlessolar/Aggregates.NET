using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IOobHandler
    {
        Task Publish<T>(string bucket, Id streamId, IEnumerable<Id> parents, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
        Task<IEnumerable<IWritableEvent>> Retrieve<T>(string bucket, Id streamId, IEnumerable<Id> parents, int? skip = null, int? take = null, bool ascending = true) where T : class, IEventSource;
    }
}
