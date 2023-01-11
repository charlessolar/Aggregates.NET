namespace Aggregates.Contracts
{
    public interface IParentDescriptor
    {
        string EntityType { get; set; }
        Id StreamId { get; set; }
    }
}
