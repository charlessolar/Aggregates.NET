using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IRepositoryFactory
    {
        IRepository<T> ForEntity<T>( IDomainUnitOfWork uow) where T : IEntity;
        IRepository<TParent, TEntity> ForEntity<TParent, TEntity>(TParent parent, IDomainUnitOfWork uow) where TEntity : IChildEntity<TParent> where TParent : IEntity;
        IPocoRepository<T> ForPoco<T>(IDomainUnitOfWork uow) where T : class, new();
        IPocoRepository<TParent, T> ForPoco<TParent, T>(TParent parent, IDomainUnitOfWork uow) where T : class, new() where TParent : IEntity;
    }
}