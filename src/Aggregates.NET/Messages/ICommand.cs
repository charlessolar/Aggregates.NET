
namespace Aggregates.Messages
{
    [Versioned("ICommand", "Aggregates", 1)]
    public interface ICommand : IMessage
    {
    }
}
