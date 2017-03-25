using System.Collections.Generic;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventSource
    {
        string Bucket { get; }
        string StreamId { get; }
        long Version { get; }

        IEventStream Stream { get; }
        void Hydrate(IEnumerable<IEvent> events);
        void Conflict(IEvent @event, IDictionary<string, string> metadata = null);
        void Apply(IEvent @event, IDictionary<string, string> metadata = null);
        void Raise(IEvent @event, IDictionary<string, string> metadata = null);
    }

    public interface IEventSource<TId> : IEventSource
    {
        TId Id { get; set; }
    }
}