using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreSnapshots
    {
        Task<ISnapshot> GetSnapshot<T>(string bucket, string streamId) where T : class, IEventSource;
        Task WriteSnapshots<T>(string bucket, string streamId, ISnapshot snapshot, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
    }
}
