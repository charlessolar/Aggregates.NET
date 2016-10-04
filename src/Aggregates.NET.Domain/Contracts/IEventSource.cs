using NServiceBus;
using System;
using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IEventSource
    {
        String Bucket { get; }
        String StreamId { get; }
        Int32 Version { get; }

        IEventStream Stream { get; }
        void Hydrate(IEnumerable<IEvent> events);
        void Conflict(IEnumerable<IEvent> events);
        void Apply<TEvent>(Action<TEvent> action) where TEvent : IEvent;
    }

    public interface IEventSource<TId> : IEventSource
    {
        TId Id { get; set; }
    }
}