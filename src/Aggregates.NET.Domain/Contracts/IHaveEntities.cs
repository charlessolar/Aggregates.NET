using Aggregates.Internal;

namespace Aggregates.Contracts
{
    public interface IHaveEntities<TParent> where TParent : State<TParent>
    {
        IRepository<TParent, T> For<T>() where T : State<T, TParent>;
        IPocoRepository<TParent, T> Poco<T>() where T : class, new();
    }
}