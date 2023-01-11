namespace Aggregates.Contracts
{
    interface INeedStore
    {
        IStoreEvents Store { get; set; }
    }
}
