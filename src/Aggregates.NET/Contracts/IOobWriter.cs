using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IOobWriter
    {
        Task<long> GetSize<TEntity>(string bucket, Id streamId, Id[] parents, string oobId) where TEntity : IEntity;
        Task<IFullEvent[]> GetEvents<TEntity>(string bucket, Id streamId, Id[] parents, string oobId, long? start = null, int? count = null) where TEntity : IEntity;
        Task<IFullEvent[]> GetEventsBackwards<TEntity>(string bucket, Id streamId, Id[] parents, string oobId, long? start = null, int? count = null) where TEntity : IEntity;

        Task WriteEvents<TEntity>(string bucket, Id streamId, Id[] parents, IFullEvent[] events, Guid CommitId, IDictionary<string, string> commitHeaders) where TEntity : IEntity;
    }
}
