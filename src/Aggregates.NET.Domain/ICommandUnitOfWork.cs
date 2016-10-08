using System;
using System.Threading.Tasks;
using NServiceBus.ObjectBuilder;

namespace Aggregates
{
    public interface ICommandUnitOfWork
    {
        IBuilder Builder { get; set; }
        // The number of times the command has been re-run due to error
        int Retries { get; set; }

        Task Begin();
        Task End(Exception ex = null);
    }
}
