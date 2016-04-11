using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEntityRepository : IRepository { }

    public interface IEntityRepository<TAggregateId, T> : IEntityRepository where T : class, IEntity
    {
        Task<T> Get<TId>(TId id);

        Task<T> New<TId>(TId id);
    }
}