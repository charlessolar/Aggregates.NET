using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IStoreSnapshots
    {
        Task<ISnapshot> GetSnapshot<T>(string bucket, Id streamId, IEnumerable<Id> parents) where T : class, IEventSource;
        Task WriteSnapshots<T>(string bucket, Id streamId, IEnumerable<Id> parents, long version, IMemento memento, IDictionary<string, string> commitHeaders) where T : class, IEventSource;
    }
}
