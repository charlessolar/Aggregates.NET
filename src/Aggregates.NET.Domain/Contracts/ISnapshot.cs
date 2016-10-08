using System;

namespace Aggregates.Contracts
{
    public interface ISnapshot
    {
        string Bucket { get; }
        string Stream { get; }
        int Version { get; }

        object Payload { get; }

        string EntityType { get; }
        DateTime Timestamp { get; }
    }
}