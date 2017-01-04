using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Contracts;

namespace Aggregates
{
    public interface IResolveConflicts
    {
        /// <summary>
        /// Entity is a clean entity without the Uncommitted events
        /// </summary>
        Task Resolve<T>(T entity, IEnumerable<IWritableEvent> uncommitted, Guid commitId, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
    }
}
