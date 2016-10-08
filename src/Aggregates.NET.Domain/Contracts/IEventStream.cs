using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventStream
    {
        object CurrentMemento { get; }
        int? LastSnapshot { get; }
        string Bucket { get; }
        string StreamId { get; }
        int StreamVersion { get; }
        int CommitVersion { get; }

        int TotalUncommitted { get; }
        /// <summary>
        /// All events read from the store
        /// </summary>
        IEnumerable<IWritableEvent> Committed { get; }
        /// <summary>
        /// Events raised but not committed 
        /// </summary>
        IEnumerable<IWritableEvent> Uncommitted { get; }
        /// <summary>
        /// OOB events raised but not committed 
        /// </summary>
        IEnumerable<IWritableEvent> OobUncommitted { get; }
        /// <summary>
        /// Snapshots taken but not committed 
        /// </summary>
        IEnumerable<ISnapshot> SnapshotsUncommitted { get; }

        /// <summary>
        /// Gets all events for the stream from the store regardless of current snapshot 
        /// </summary>
        Task<IEnumerable<IWritableEvent>> AllEvents(bool? backwards);
        /// <summary>
        /// Gets all OOB events for the stream from the store
        /// </summary>
        Task<IEnumerable<IWritableEvent>> OobEvents(bool? backwards);

        void Add(IEvent @event, IDictionary<string, string> headers);
        void AddOutOfBand(IEvent @event, IDictionary<string, string> headers);
        void AddSnapshot(object memento, IDictionary<string, string> headers);
        void Concat(IEnumerable<IWritableEvent> events);
        Task<Guid> Commit(Guid commitId, Guid startingEventId, IDictionary<string, string> commitHeaders);

        IEventStream Clone(IWritableEvent @event = null);
        void Flush(bool committed);
    }
}