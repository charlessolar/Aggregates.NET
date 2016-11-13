using System;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class Snapshot : ISnapshot
    {
        public string Bucket { get; set; }
        public string Stream { get; set; }
        public int Version { get; set; }
        public object Payload { get; set; }

        public string EntityType { get; set; }
        public DateTime Timestamp { get; set; }
    }
}