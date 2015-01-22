using NEventStore;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IRepositoryFactory
    {
        IRepository<TAggregate> ForAggregate<TAggregate>(IBuilder builder, IStoreEvents store) where TAggregate : class, IAggregate;

        IEntityRepository<TAggregateId, TEntity> ForEntity<TAggregateId, TEntity>(TAggregateId AggregateId, IBuilder builder, IEventStream stream) where TEntity : class, IEntity;
    }
}