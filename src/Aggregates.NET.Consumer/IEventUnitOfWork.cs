using Aggregates.Contracts;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IEventUnitOfWork
    {
        IBuilder Builder { get; set; }
        // The number of times the event has been re-run due to error
        Int32 Retries { get; set; }

        Task Begin();
        Task End(Exception ex = null);
    }
}
