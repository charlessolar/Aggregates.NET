using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreSnapshots
    {
        Task<ISnapshot> GetSnapshot<T>(String bucket, String streamId) where T : class, IEventSource;
        Task WriteSnapshots<T>(String bucket, String streamId, IEnumerable<ISnapshot> snapshots, IDictionary<String, String> commitHeaders) where T : class, IEventSource;
    }
}
