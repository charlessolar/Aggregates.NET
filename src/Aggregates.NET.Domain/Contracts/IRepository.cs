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
        void Commit(Guid commitId, IDictionary<String, Object> headers);
    }

    public interface IRepository<T> : IRepository where T : class, IEntity
    {
        T Get<TId>(TId id);

        T Get<TId>(String bucketId, TId id);

        T New<TId>(String bucketId, TId id);

        T New<TId>(TId id);
    }
}