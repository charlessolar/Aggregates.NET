namespace Aggregates.Contracts
{
    public interface IHaveEntities<TBase, TId> where TBase : class, IBase<TId>
    {
        IEntityRepository<TBase, TId, T> For<T>() where T : class, IEntity;
        IPocoRepository<TBase, TId, T> Poco<T>() where T : class, new();
    }
}