using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStorePocos
    {
        Task Evict<T>(String bucket, String streamId) where T : class;
        Task<T> Get<T>(String bucket, String stream) where T : class;
        Task Write<T>(T poco, String bucket, String stream, IDictionary<String, String> commitHeaders);
    }
}
