using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IRepositoryFactory
    {
        IRepository<T> ForEntity<T>() where T : IEntity;
        IRepository<TEntity, TParent> ForEntity<TEntity, TParent>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IEntity;
    }
}