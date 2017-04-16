using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreEvents
    {
        Task<IEnumerable<IFullEvent>> GetEvents(string stream, long? start = null, int? count = null);
        Task<IEnumerable<IFullEvent>> GetEventsBackwards(string stream, long? start = null, int? count = null);
        /// <summary>
        /// Returns the next expected version of the stream
        /// </summary>
        Task<long> WriteEvents(string stream, IEnumerable<IFullEvent> events, IDictionary<string, string> commitHeaders, long? expectedVersion = null);

        Task<long> Size(string stream);
        Task<long> WriteSnapshot(string stream, IFullEvent snapshot, IDictionary<string, string> commitheaders);
        Task WriteMetadata(string stream, long? maxCount = null, long? truncateBefore = null, TimeSpan? maxAge = null, TimeSpan? cacheControl = null, bool? frozen = null, Guid? owner = null, bool force = false, IDictionary<string, string> custom = null);
        Task<string> GetMetadata(string stream, string key);
        Task<bool> IsFrozen(string stream);
    }
}
