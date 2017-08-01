using Aggregates.Internal;
using NServiceBus.ObjectBuilder;

namespace Aggregates.Contracts
{
    public interface IRepositoryFactory
    {
        IRepository<T> ForAggregate<T>(IBuilder builder) where T : Aggregate<T>;
        IRepository<TParent, TEntity> ForEntity<TParent, TEntity>(TParent parent, IBuilder builder) where TEntity : Aggregates.Entity<TEntity, TParent> where TParent : Internal.Entity<TParent>;
        IPocoRepository<T> ForPoco<T>(IBuilder builder) where T : class, new();
        IPocoRepository<TParent, T> ForPoco<TParent, T>(TParent parent, IBuilder builder) where T : class, new() where TParent : Internal.Entity<TParent>;
    }
}