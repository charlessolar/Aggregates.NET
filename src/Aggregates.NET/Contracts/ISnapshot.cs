using System;

namespace Aggregates.Contracts
{
    public interface ISnapshot
    {
        string Bucket { get; }
        string StreamId { get; }
        int Version { get; }

        object Payload { get; }

        string EntityType { get; }
        DateTime Timestamp { get; }
    }
}