using System;
using System.Threading.Tasks;
using NServiceBus.ObjectBuilder;

namespace Aggregates
{
    public interface IEventUnitOfWork
    {
        IBuilder Builder { get; set; }
        // The number of times the event has been re-run due to error
        int Retries { get; set; }

        Task Begin();
        Task End(Exception ex = null);
    }
}
