

namespace Aggregates.Contracts
{
    public interface IHaveEntities<TParent> where TParent : Internal.Entity<TParent>
    {
        IRepository<TParent, T> For<T>() where T : Aggregates.Entity<T, TParent>;
        IPocoRepository<TParent, T> Poco<T>() where T : class, new();
    }
}