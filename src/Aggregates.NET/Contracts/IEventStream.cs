using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aggregates.Internal;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IImmutableEventStream
    {
        ISnapshot Snapshot { get; }

        Id StreamId { get; }
        string Bucket { get; }
        IEnumerable<Id> Parents { get; }
        IEnumerable<OobDefinition> Oobs { get; }

        long StreamVersion { get; }
        long CommitVersion { get; }

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
        IMemento PendingSnapshot { get; }

        IEventStream Clone();
    }
    public interface IEventStream : IImmutableEventStream
    {
        void Add(IEvent @event, IDictionary<string, string> headers);
        void AddSnapshot(IMemento memento);
        void AddOob(IEvent @event, string id, IDictionary<string, string> metadata);
        void DefineOob(string id, bool transient = false, int? daysToLive = null);
    }
}