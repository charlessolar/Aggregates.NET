using System.Collections.Generic;
using NServiceBus;

namespace Aggregates.Contracts
{
    public interface IEventSource
    {
        Id Id { get; }
        long Version { get; }
        IEventSource Parent { get; }

    }

    internal interface IEventSourced : IEventSource         
    {

        IEventStream Stream { get; }
        void Hydrate(IEnumerable<IEvent> events);
        void Conflict(IEvent @event, IDictionary<string, string> metadata = null);
        void Apply(IEvent @event, IDictionary<string, string> metadata = null);
        void Raise(IEvent @event, IDictionary<string, string> metadata = null);
    }

}