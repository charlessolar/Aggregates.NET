using Aggregates.Internal;

namespace Aggregates.Contracts
{
    interface IHaveEntities<TParent> where TParent : IEntity
    {
        IRepository<T, TParent> For<T>() where T : IChildEntity<TParent>;
        IPocoRepository<T, TParent> Poco<T>() where T : class, new();
    }
}