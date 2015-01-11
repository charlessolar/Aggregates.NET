using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.NET.NEventStore
{
    public class RepositoryFactory : IRepositoryFactory
    {
        public IRepository<T> ForAggregate<T>(IBuilder builder) where T : class, IAggregate
        {
            var repoType = typeof(Repository<>).MakeGenericType(typeof(T));
            return (IRepository<T>)Activator.CreateInstance(repoType, builder);
        }

        public IEntityRepository<T> ForEntity<T>(IBuilder builder) where T : class, IEntity
        {
            var repoType = typeof(EntityRepository<>).MakeGenericType(typeof(T));
            return (IEntityRepository<T>)Activator.CreateInstance(repoType, builder);
        }
    }
}