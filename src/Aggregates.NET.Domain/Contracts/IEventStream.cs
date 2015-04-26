using EventStore.ClientAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventStream
    {
        String BucketId { get; }
        String StreamId { get; }
        Int32 StreamVersion { get; }

        ISnapshot Snapshot { get; }

        IEnumerable<Object> Events { get; }

        void Add(Object @event, Int32 expectedVersion, IDictionary<String, Object> headers);
        void Commit(IStoreEvents store, Guid commitId, IDictionary<String, Object> commitHeaders);

        void ClearChanges();
    }
}