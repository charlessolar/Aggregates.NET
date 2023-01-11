using Aggregates.Contracts;
using System;

namespace Aggregates.Internal
{
    public class Snapshot : ISnapshot
    {
        public string Bucket { get; set; }
        public Id StreamId { get; set; }
        public IParentDescriptor[] Parents { get; set; }
        public long Version { get; set; }
        public object Payload { get; set; }

        public string EntityType { get; set; }
        public DateTime Timestamp { get; set; }
    }
}