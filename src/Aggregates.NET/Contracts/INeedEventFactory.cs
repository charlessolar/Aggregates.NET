
namespace Aggregates.Contracts
{
    interface INeedEventFactory
    {
        IEventFactory EventFactory { get; set; }
    }
}
