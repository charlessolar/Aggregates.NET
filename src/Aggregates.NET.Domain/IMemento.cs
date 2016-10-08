namespace Aggregates
{
    public interface IMemento<out TId>
    {
        TId EntityId { get; }
    }
}
