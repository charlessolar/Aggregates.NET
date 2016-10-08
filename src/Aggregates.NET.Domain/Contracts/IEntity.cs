namespace Aggregates.Contracts
{
    public interface IEntity : IEventSource
    {
    }
    
    public interface IEntity<TId, TParent, TParentId> : IEntity where TParent : class, IBase<TParentId>
    {
        TParent Parent { get; set; }
    }
}