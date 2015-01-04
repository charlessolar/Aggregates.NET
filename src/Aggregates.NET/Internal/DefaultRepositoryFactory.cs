using Aggregates.Contracts;
using NEventStore;
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
        public IRepository<T> ForAggregate<T>(IBuilder builder, IStoreEvents store) where T : class, IAggregate
        {
            var repoType = typeof(Repository<>).MakeGenericType(typeof(T));
            return (IRepository<T>)Activator.CreateInstance(repoType, builder, store);
        }
        public IEntityRepository<T> ForEntity<T>(IBuilder builder, IEventStream stream) where T : class, IEntity
        {
            var repoType = typeof(EntityRepository<>).MakeGenericType(typeof(T));
            return (IEntityRepository<T>)Activator.CreateInstance(repoType, builder, stream);
        }
    }
}
