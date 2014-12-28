using NEventStore;
using System;
using System.Collections.Generic;
namespace Aggregates.Contracts
{
    public interface IEventSourceBase
    {
        String StreamId { get; }
        String BucketId { get; }
        Int32 Version { get; }

        void Hydrate(IEnumerable<object> events);
        
        void Apply<TEvent>(Action<TEvent> action);
    }
    public interface ISnapshottingEventSourceBase : IEventSourceBase
    {
        void RestoreSnapshot(ISnapshot snapshot);
        ISnapshot TakeSnapshot();
        Boolean ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion);
    }
    public interface IEventSource<TId> : IEventSourceBase
    {
        TId Id { get; }
    }
    public interface ISnapshottingEventSource<TId> : ISnapshottingEventSourceBase, IEventSource<TId>
    {
    }
}
