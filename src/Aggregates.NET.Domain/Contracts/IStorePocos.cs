using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStorePocos
    {
        Task Evict<T>(string bucket, Id streamId, IEnumerable<Id> parents) where T : class;
        Task<T> Get<T>(string bucket, Id streamId, IEnumerable<Id> parents) where T : class;
        Task Write<T>(T poco, string bucket, Id streamId, IEnumerable<Id> parents, IDictionary<string, string> commitHeaders);
    }
}
