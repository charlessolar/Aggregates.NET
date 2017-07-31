using System.Collections.Generic;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventSource
    {
        Id Id { get; }
        long Version { get; }
        IEventSource Parent { get; }

        IEventStream Stream { get; }

        void Hydrate(IEnumerable<IEvent> events);
    }

    internal interface IEventSourced : IEventSource         
    {
        void Conflict(IEvent @event, IDictionary<string, string> metadata = null);
        void Apply(IEvent @event, IDictionary<string, string> metadata = null);
        void Raise(IEvent @event, string id, IDictionary<string, string> metadata = null);
    }

}