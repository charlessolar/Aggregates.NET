using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IOobHandler
    {
        Task Publish<T>(string bucket, Id streamId, IEnumerable<Id> parents, IEnumerable<IFullEvent> events, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
        Task<IEnumerable<IFullEvent>> Retrieve<T>(string bucket, Id streamId, IEnumerable<Id> parents, long? skip = null, int? take = null, bool ascending = true) where T : class, IEventSource;
        Task<long> Size<T>(string bucket, Id streamId, IEnumerable<Id> parents) where T : class, IEventSource;
    }
}
