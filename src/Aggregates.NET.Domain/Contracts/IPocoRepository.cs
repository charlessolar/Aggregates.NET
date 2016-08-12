using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IPocoRepository<T> : IRepository where T : class, new()
    {
        Task<T> Get<TId>(TId Id);
        Task<T> Get<TId>(String bucketId, TId id);
        Task<T> TryGet<TId>(TId id);
        Task<T> TryGet<TId>(String bucketId, TId id);

        Task<T> New<TId>(String bucketId, TId id);
        Task<T> New<TId>(TId id);
    }
    public interface IPocoRepository<TBase, TBaseId, T> : IRepository where TBase : IBase<TBaseId> where T : class, new()
    {
    }
}
