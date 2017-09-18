using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IRepositoryFactory
    {
        IRepository<T> ForEntity<T>() where T : IEntity;
        IRepository<TParent, TEntity> ForEntity<TParent, TEntity>(TParent parent) where TEntity : IChildEntity<TParent> where TParent : IEntity;
        IPocoRepository<T> ForPoco<T>() where T : class, new();
        IPocoRepository<TParent, T> ForPoco<TParent, T>(TParent parent) where T : class, new() where TParent : IEntity;
    }
}