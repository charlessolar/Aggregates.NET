using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IMessageDispatcher
    {
        Task SendLocal(IFullMessage message, IDictionary<string, string> headers = null);
    }
}
