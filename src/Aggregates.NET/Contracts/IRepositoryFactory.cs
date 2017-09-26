using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IRepositoryFactory
    {
        IRepository<T> ForEntity<T>( IDomainUnitOfWork uow) where T : IEntity;
        IRepository<TEntity, TParent> ForEntity<TEntity, TParent>(TParent parent, IDomainUnitOfWork uow) where TEntity : IChildEntity<TParent> where TParent : IEntity;
        IPocoRepository<T> ForPoco<T>(IDomainUnitOfWork uow) where T : class, new();
        IPocoRepository<T, TParent> ForPoco<T, TParent>(TParent parent, IDomainUnitOfWork uow) where T : class, new() where TParent : IEntity;
    }
}