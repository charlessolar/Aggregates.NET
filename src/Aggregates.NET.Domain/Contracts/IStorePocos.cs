using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStorePocos
    {
        Task Evict<T>(string bucket, string streamId) where T : class;
        Task<T> Get<T>(string bucket, string stream) where T : class;
        Task Write<T>(T poco, string bucket, string stream, IDictionary<string, string> commitHeaders);
    }
}
