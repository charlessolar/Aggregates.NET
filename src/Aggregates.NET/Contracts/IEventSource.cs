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

        void RestoreSnapshot(ISnapshot memento);
        ISnapshot TakeSnapshot();

        void Apply<TEvent>(Action<TEvent> action);
    }
    public interface IEventSource<TId> : IEventSourceBase
    {
        TId Id { get; }
    }
}
