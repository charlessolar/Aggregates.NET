using Aggregates.Internal;

namespace Aggregates.Contracts
{
    interface IHaveEntities<TParent> where TParent : IEntity
    {
        IRepository<TParent, T> For<T>() where T : IChildEntity<TParent>;
        IPocoRepository<TParent, T> Poco<T>() where T : class, new();
    }
}