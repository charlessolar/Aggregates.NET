using System;
using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IEventDescriptor
    {
        // can be set right before saving
        Guid EventId { get; }
        IDictionary<string, string> CommitHeaders { get; }
        bool Compressed { get; }

        string EntityType { get; }
        string StreamType { get; }
        string Bucket { get; }
        Id StreamId { get; }
        Id[] Parents { get; }

        long Version { get; }
        DateTime Timestamp { get; }

        IDictionary<string, string> Headers { get; }
    }
}