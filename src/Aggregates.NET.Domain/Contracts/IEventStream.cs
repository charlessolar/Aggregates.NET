using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventStream
    {
        Object CurrentMemento { get; }
        Int32? LastSnapshot { get; }
        String Bucket { get; }
        String StreamId { get; }
        Int32 StreamVersion { get; }
        Int32 CommitVersion { get; }

        Int32 TotalUncommitted { get; }
        /// <summary>
        /// All events read from the store
        /// </summary>
        IEnumerable<IWritableEvent> Events { get; }
        /// <summary>
        /// Events raised but not committed 
        /// </summary>
        IEnumerable<IWritableEvent> Uncommitted { get; }
        /// <summary>
        /// OOB events raised but not committed 
        /// </summary>
        IEnumerable<IWritableEvent> OOBUncommitted { get; }
        /// <summary>
        /// Snapshots taken but not committed 
        /// </summary>
        IEnumerable<ISnapshot> SnapshotsUncommitted { get; }

        /// <summary>
        /// Gets all events for the stream from the store regardless of current snapshot 
        /// </summary>
        Task<IEnumerable<IWritableEvent>> AllEvents(Boolean? backwards);
        /// <summary>
        /// Gets all OOB events for the stream from the store
        /// </summary>
        Task<IEnumerable<IWritableEvent>> OOBEvents(Boolean? backwards);

        void Add(IEvent @event, IDictionary<String, String> headers);
        void AddOutOfBand(IEvent @event, IDictionary<String, String> headers);
        void AddSnapshot(Object memento, IDictionary<String, String> headers);
        void Concat(IEnumerable<IWritableEvent> events);
        Task<Guid> Commit(Guid commitId, Guid startingEventId, IDictionary<String, String> commitHeaders);

        IEventStream Clone(IWritableEvent @event = null);
        void Flush(Boolean committed);
    }
}