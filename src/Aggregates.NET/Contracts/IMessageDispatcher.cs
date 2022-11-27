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
    }
}
