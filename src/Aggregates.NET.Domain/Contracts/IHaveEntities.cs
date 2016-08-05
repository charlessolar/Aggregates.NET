using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IHaveEntities<TBase, TId> where TBase : class, IBase<TId>
    {
        IEntityRepository<TBase, TId, T> For<T>() where T : class, IEntity;
    }
}