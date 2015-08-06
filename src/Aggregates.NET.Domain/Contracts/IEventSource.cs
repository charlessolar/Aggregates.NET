using System;
using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IEventSource
    {
        String Bucket { get; }
        String StreamId { get; }
        Int32 Version { get; }

        void Hydrate(IEnumerable<object> events);
        void Apply<TEvent>(Action<TEvent> action);
    }

    public interface IEventSource<TId> : IEventSource
    {
        TId Id { get; set; }
    }
}