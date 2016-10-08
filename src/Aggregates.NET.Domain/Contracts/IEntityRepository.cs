using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEntityRepository : IRepository { }

    public interface IEntityRepository<TParent, TParentId, T> : IEntityRepository where T : class, IEntity where TParent : class, IBase<TParentId>
    {
        /// <summary>
        /// Attempts to get entity from store, if stream does not exist it throws
        /// </summary>
        Task<T> Get<TId>(TId id);

        /// <summary>
        /// Attempts to retreive entity from store, if stream does not exist it does not throw
        /// </summary>
        Task<T> TryGet<TId>(TId id);

        /// <summary>
        /// Initiates a new event stream
        /// </summary>
        Task<T> New<TId>(TId id);
    }
}