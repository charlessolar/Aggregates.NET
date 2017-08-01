namespace Aggregates.Contracts
{
    public interface INeedStream
    {
        IEventStream Stream { get; set; }
    }
}