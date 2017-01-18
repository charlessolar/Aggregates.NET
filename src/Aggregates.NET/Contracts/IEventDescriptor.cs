using System;
using System.Collections.Generic;

namespace Aggregates.Contracts
{
    public interface IEventDescriptor
    {
        // can be set right before saving
        Guid EventId { get; set; }
        IDictionary<string, string> CommitHeaders { get; set; }
        bool Compressed { get; set; }

        string EntityType { get; }
        string StreamType { get; }
        string Bucket { get; }
        string StreamId { get; }

        int Version { get; }
        DateTime Timestamp { get; }

        IDictionary<string, string> Headers { get; }
    }
}