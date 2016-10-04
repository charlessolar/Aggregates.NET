using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStorePocos
    {
        Task<T> Get<T>(String bucket, String stream, Boolean tryCache = true) where T : class;
        Task Write<T>(T poco, String bucket, String stream, IDictionary<String, String> commitHeaders);
    }
}
