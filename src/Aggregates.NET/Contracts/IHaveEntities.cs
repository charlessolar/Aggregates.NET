using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IHaveEntities<TParent> : IEntity where TParent : IEntity
    {
        IRepository<T, TParent> For<T>() where T : class, IChildEntity<TParent>;
    }
}