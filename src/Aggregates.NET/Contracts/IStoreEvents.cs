using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreEvents
    {
        Task<IEnumerable<IWritableEvent>> GetEvents(string stream, int? start = null, int? count = null);
        Task<IEnumerable<IWritableEvent>> GetEventsBackwards(string stream, int? start = null, int? count = null);
        /// <summary>
        /// Returns the next expected version of the stream
        /// </summary>
        Task<int> WriteEvents(string stream, IEnumerable<IWritableEvent> events, IDictionary<string, string> commitHeaders, int? expectedVersion = null);

        Task<int> WriteSnapshot(string stream, IWritableEvent snapshot, IDictionary<string, string> commitheaders);
        Task WriteMetadata(string stream, int? maxCount = null, int? truncateBefore = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null, bool? frozen = null, Guid? owner = null, bool force = false, IDictionary<string, string> custom = null);
        Task<string> GetMetadata(string stream, string key);
        Task<bool> IsFrozen(string stream);
    }
}
