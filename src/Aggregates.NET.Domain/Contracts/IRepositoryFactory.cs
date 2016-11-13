using NServiceBus.ObjectBuilder;

namespace Aggregates.Contracts
{
    public interface IRepositoryFactory
    {
        IRepository<TAggregate> ForAggregate<TAggregate>(IBuilder builder) where TAggregate : class, IAggregate;
        IEntityRepository<TParent, TParentId, TEntity> ForEntity<TParent, TParentId, TEntity>(TParent parent, IBuilder builder) where TEntity : class, IEntity where TParent : class, IBase<TParentId>;
        IPocoRepository<T> ForPoco<T>(IBuilder builder) where T : class, new();
        IPocoRepository<TParent, TParentId, T> ForPoco<TParent, TParentId, T>(TParent parent, IBuilder builder) where T : class, new() where TParent : class, IBase<TParentId>;
    }
}