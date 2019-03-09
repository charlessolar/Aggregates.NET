using System;
using System.Collections.Generic;
using Aggregates.Contracts;

namespace Aggregates.Internal
{
    class EventDescriptor : IEventDescriptor
    {
        public Guid EventId { get; set; }

        public string EntityType { get; set; }

        public string StreamType { get; set; }
        public string Bucket { get; set; }
        public Id StreamId { get; set; }
        public IParentDescriptor[] Parents { get; set; }

        public bool Compressed { get; set; }
        public long Version { get; set; }
        public DateTime Timestamp { get; set; }

        public IDictionary<string, string> Headers { get; set; }
        public IDictionary<string, string> CommitHeaders { get; set; }
    }
}