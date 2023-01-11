using Aggregates.Messages;
using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IFullMessage
    {
        IMessage Message { get; }
        IDictionary<string, string> Headers { get; }
    }
}
