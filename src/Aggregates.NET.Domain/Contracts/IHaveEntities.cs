using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IHaveEntities<TAggregateId>
    {
        IEntityRepository<TAggregateId, T> E<T>() where T : class, IEntity;

        IEntityRepository<TAggregateId, T> Entity<T>() where T : class, IEntity;
    }
}