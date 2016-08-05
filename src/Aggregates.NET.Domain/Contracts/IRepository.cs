using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IRepository : IDisposable
    {
        Task Commit(Guid commitId, IDictionary<String, String> headers);
    }

    public interface IRepository<T> : IRepository where T : class, IEventSource
    {
        Task<T> Get<TId>(TId id);

        Task<T> Get<TId>(String bucketId, TId id);

        Task<T> New<TId>(String bucketId, TId id);

        Task<T> New<TId>(TId id);
    }
}