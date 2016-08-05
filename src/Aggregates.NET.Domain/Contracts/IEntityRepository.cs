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
        Task<T> Get<TId>(TId id);
        Task<T> TryGet<TId>(TId id);

        Task<T> New<TId>(TId id);
    }
}