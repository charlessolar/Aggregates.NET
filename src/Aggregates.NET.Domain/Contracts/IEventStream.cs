using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventStream
    {
        IMemento CurrentMemento { get; }
        ISnapshot Snapshot { get; }

        Id StreamId { get; }
        string Bucket { get; }
        string StreamType { get; }
        IEnumerable<Id> Parents { get; }
        string StreamName { get; }

        long StreamVersion { get; }
        long CommitVersion { get; }

        Task<long> Size { get; }
        Task<long> OobSize { get; }

        /// <summary>
        /// Indicates whether the stream has been changed
        /// </summary>
        bool Dirty { get; }

        /// <summary>
        /// The total number of events and snapshots not saved yet
        /// </summary>
        int TotalUncommitted { get; }
        /// <summary>
        /// All events read from the store
        /// </summary>
        IEnumerable<IFullEvent> Committed { get; }
        /// <summary>
        /// Events raised but not committed 
        /// </summary>
        IEnumerable<IFullEvent> Uncommitted { get; }
        /// <summary>
        /// OOB events raised but not committed 
        /// </summary>
        IEnumerable<IFullEvent> OobUncommitted { get; }

        /// <summary>
        /// Retrieves a slice of the event stream 
        /// </summary>
        Task<IEnumerable<IFullEvent>> Events(long? start = null, int? count = null);

        /// <summary>
        /// Retreives a slice of the oob event stream
        /// </summary>
        Task<IEnumerable<IFullEvent>> OobEvents(long? start = null, int? count = null);

        void Add(IEvent @event, IDictionary<string, string> headers);
        void AddOutOfBand(IEvent @event, IDictionary<string, string> headers);
        void AddSnapshot(IMemento memento);
        void Concat(IEnumerable<IFullEvent> events);
        Task Commit(Guid commitId, IDictionary<string, string> commitHeaders);
        Task VerifyVersion(Guid commitId);

        IEventStream Clone();
        void Flush(bool committed);
    }
}