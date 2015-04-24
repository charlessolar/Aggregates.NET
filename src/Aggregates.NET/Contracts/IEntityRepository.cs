using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEntityRepository { }

    public interface IEntityRepository<TAggregateId, T> : IEntityRepository where T : class, IEventSource
    {
        T Get<TId>(TId id);

        T Get<TId>(TId id, Int32 version);

        T New<TId>(TId id);
    }
}