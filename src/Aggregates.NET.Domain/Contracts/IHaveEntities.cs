using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IHaveEntities<TParent> where TParent : Entity<TParent>
    {
        IRepository<TParent, T> For<T>() where T : Entity<T, TParent>;
        IPocoRepository<TParent, T> Poco<T>() where T : class, new();
    }
}