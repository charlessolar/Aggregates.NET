using System;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class Snapshot : ISnapshot
    {
        public string Bucket { get; set; }
        public Id StreamId { get; set; }
        public long Version { get; set; }
        public IMemento Payload { get; set; }

        public string EntityType { get; set; }
        public DateTime Timestamp { get; set; }
    }
}