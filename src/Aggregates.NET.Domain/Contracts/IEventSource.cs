using System.Collections.Generic;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventSource
    {
        string Bucket { get; }
        string StreamId { get; }
        int Version { get; }

        IEventStream Stream { get; }
        void Hydrate(IEnumerable<IEvent> events);
        void Conflict(IEvent @event);
        void Apply(IEvent @event);
        void Raise(IEvent @event);
    }

    public interface IEventSource<TId> : IEventSource
    {
        TId Id { get; set; }
    }
}