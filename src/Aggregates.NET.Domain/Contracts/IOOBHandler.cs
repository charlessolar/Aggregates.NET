using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IOOBHandler
    {
        Task Publish<T>(String Bucket, String StreamId, IEnumerable<IWritableEvent> Events, IDictionary<String, String> commitHeaders) where T : class, IEventSource;
        Task<IEnumerable<IWritableEvent>> Retrieve<T>(String Bucket, String StreamId, Int32? Skip = null, Int32? Take = null, Boolean Ascending = true) where T : class, IEventSource;
    }
}
