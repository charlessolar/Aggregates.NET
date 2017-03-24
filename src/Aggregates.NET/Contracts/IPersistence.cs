using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus.Extensibility;

namespace Aggregates.Contracts
{
    // Saves context bags between message retries
    public interface IPersistence
    {
        Task Save(string messageId, Type uowType, ContextBag bag);
        Task<List<Tuple<Type, ContextBag>>> Remove(string messageId);
    }
}
