using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public interface IResolveConflicts
    {
        /// <summary>
        /// Entity is a clean entity without the Uncommitted events
        /// </summary>
        Task<Guid> Resolve<T>(T Entity, IEnumerable<IWritableEvent> Uncommitted, Guid commitId, Guid startingEventId, IDictionary<String, String> commitHeaders) where T : class, IEventSource;
    }
}
