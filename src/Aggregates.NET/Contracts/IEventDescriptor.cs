using System;
using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IEventDescriptor
    {
        Guid EventId { get; }
        string EntityType { get; }

        bool Compressed { get; }
        int Version { get; }
        DateTime Timestamp { get; }

        IDictionary<string, string> Headers { get; }
    }
}