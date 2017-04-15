using NServiceBus.ObjectBuilder;

namespace Aggregates.Contracts
{
    public interface IRepositoryFactory
    {
        IRepository<TAggregate> ForAggregate<TAggregate>(IBuilder builder) where TAggregate : class, IAggregate;
        IRepository<TParent, TEntity> ForEntity<TParent, TEntity>(TParent parent, IBuilder builder) where TEntity : class, IEntity where TParent : class, IBase;
        IPocoRepository<T> ForPoco<T>(IBuilder builder) where T : class, new();
        IPocoRepository<TParent, T> ForPoco<TParent, T>(TParent parent, IBuilder builder) where T : class, new() where TParent : class, IBase;
    }
}