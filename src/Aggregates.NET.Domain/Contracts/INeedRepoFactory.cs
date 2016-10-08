namespace Aggregates.Contracts
{
    public interface INeedRepositoryFactory
    {
        IRepositoryFactory RepositoryFactory { get; set; }
    }
}
