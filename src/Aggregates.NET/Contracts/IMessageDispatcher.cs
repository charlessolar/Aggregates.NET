using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Messages;

namespace Aggregates.Contracts
{
    public interface IMessageDispatcher
    {
        Task SendLocal(IFullMessage message, IDictionary<string, string> headers = null);
        Task SendLocal(IFullMessage[] messages, IDictionary<string, string> headers = null);
        Task Send(IFullMessage[] message, string destination);
        Task Publish(IFullMessage[] message);
        Task SendToError(IFullMessage message);
    }
}
