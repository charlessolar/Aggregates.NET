namespace Aggregates.Contracts
{
    public interface IEntity : IEventSource
    {
    }
    
    public interface IEntity<TParent> : IEntity where TParent : class, IBase
    {
        new TParent Parent { get; set; }
    }
}