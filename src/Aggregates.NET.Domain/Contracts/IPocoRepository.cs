using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IPocoRepository<T> : IRepository where T : class, new()
    {
        Task<T> Get<TId>(TId id);
        Task<T> Get<TId>(string bucketId, TId id);
        Task<T> TryGet<TId>(TId id);
        Task<T> TryGet<TId>(string bucketId, TId id);

        Task<T> New<TId>(string bucketId, TId id);
        Task<T> New<TId>(TId id);
    }
    public interface IPocoRepository<TBase, TBaseId, T> : IRepository where TBase : IBase<TBaseId> where T : class, new()
    {
    }
}
