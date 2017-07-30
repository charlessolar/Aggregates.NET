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
        ContextBag Bag { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="retry">The number of times the event has been re-run due to error</param>
        /// <param name="bag">Will be persisted across retries</param>
        /// <returns></returns>
        Task Begin(IBuilder builder, int retry, ContextBag bag);
        Task End(Exception ex = null);
    }
    public interface ILastApplicationUnitOfWork : IApplicationUnitOfWork { }
}
