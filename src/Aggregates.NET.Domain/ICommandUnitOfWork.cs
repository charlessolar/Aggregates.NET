using System;
using System.Threading.Tasks;
using NServiceBus.Extensibility;
using NServiceBus.ObjectBuilder;

namespace Aggregates
{
    public interface ICommandUnitOfWork
    {
        IBuilder Builder { get; set; }
        // The number of times the command has been re-run due to error
        int Retries { get; set; }
        // Will be persisted across retries
        ContextBag Bag { get; set; }

        Task Begin();
        Task End(Exception ex = null);
    }
}
