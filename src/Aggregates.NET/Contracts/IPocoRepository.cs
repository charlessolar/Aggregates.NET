using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IPocoRepository<T> : IRepository where T : class, new()
    {
        Task<T> Get(Id id);
        Task<T> Get(string bucketId, Id id);
        Task<T> TryGet(Id id);
        Task<T> TryGet(string bucketId, Id id);

        Task<T> New(string bucketId, Id id);
        Task<T> New(Id id);
    }
    public interface IPocoRepository<T, TParent> : IRepository where TParent : IEntity where T : class, new()
    {
        Task<T> Get(Id id);
        Task<T> TryGet(Id id);
        Task<T> New(Id id);
    }
}
