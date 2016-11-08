using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus.Extensibility;
using NServiceBus.ObjectBuilder;

namespace Aggregates
{
    public interface IApplicationUnitOfWork
    {
        IBuilder Builder { get; set; }
        // The number of times the event has been re-run due to error
        int Retries { get; set; }
        // Will be persisted across retries
        ContextBag Bag { get; set; }

        Task Begin();
        Task End(Exception ex = null);
    }
}
