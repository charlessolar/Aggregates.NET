using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreSnapshots
    {
        Task Evict<T>(string bucket, string streamId) where T : class, IEventSource;
        Task<ISnapshot> GetSnapshot<T>(string bucket, string streamId) where T : class, IEventSource;
        Task WriteSnapshots<T>(string bucket, string streamId, IEnumerable<ISnapshot> snapshots, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
    }
}
