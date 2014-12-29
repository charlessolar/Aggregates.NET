using NEventStore;
using System;
using System.Collections.Generic;
namespace Aggregates.Contracts
{
    public interface IEventSource : INeedStream
    {
        String StreamId { get; }
        Int32 Version { get; }

        void Hydrate(IEnumerable<object> events);
        void Apply<TEvent>(Action<TEvent> action);
    }
    public interface ISnapshottingEventSource : IEventSource
    {
        void RestoreSnapshot(ISnapshot snapshot);
        ISnapshot TakeSnapshot();
        Boolean ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion);
    }
    public interface IEventSource<TId> : IEventSource
    {
        TId Id { get; }
        String BucketId { get; }
    }
    public interface ISnapshottingEventSource<TId> : ISnapshottingEventSource, IEventSource<TId>
    {
    }
}
