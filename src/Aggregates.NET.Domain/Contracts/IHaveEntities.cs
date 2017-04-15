namespace Aggregates.Contracts
{
    public interface IHaveEntities<TParent> where TParent : class, IBase
    {
        IRepository<TParent, T> For<T>() where T : class, IEntity;
        IPocoRepository<TParent, T> Poco<T>() where T : class, new();
    }
}