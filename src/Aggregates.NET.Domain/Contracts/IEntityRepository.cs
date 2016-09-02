using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEntityRepository : IRepository { }

    public interface IEntityRepository<TParent, TParentId, T> : IEntityRepository where T : class, IEntity where TParent : class, IBase<TParentId>
    {
        /// <summary>
        /// Attempts to get entity from store, if stream does not exist it throws
        /// </summary>
        /// <typeparam name="TId"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> Get<TId>(TId id);

        /// <summary>
        /// Attempts to retreive entity from store, if stream does not exist it does not throw
        /// </summary>
        /// <typeparam name="TId"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> TryGet<TId>(TId id);

        /// <summary>
        /// Initiates a new event stream
        /// </summary>
        /// <typeparam name="TId"></typeparam>
        /// <param name="bucketId"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<T> New<TId>(TId id);
    }
}