using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class DefaultRepositoryFactory : IRepositoryFactory
    {
        public IRepository<T> ForAggregate<T>(IBuilder builder) where T : class, IAggregate
        {
            var repoType = typeof(Repository<>).MakeGenericType(typeof(T));
            return (IRepository<T>)Activator.CreateInstance(repoType, builder);
        }

        public IEntityRepository<TAggregateId, T> ForEntity<TAggregateId, T>(TAggregateId aggregateId, IEventStream aggregateStream, IBuilder builder) where T : class, IEntity
        {
            var repoType = typeof(EntityRepository<,>).MakeGenericType(typeof(TAggregateId), typeof(T));
            return (IEntityRepository<TAggregateId, T>)Activator.CreateInstance(repoType, aggregateId, aggregateStream, builder);
        }
    }
}