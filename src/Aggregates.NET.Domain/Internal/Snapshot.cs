using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates;

namespace Aggregates.Internal
{
    public class Snapshot : ISnapshot
    {
        public Snapshot(string streamId, int streamRevision, object payload)
            : this(Aggregates.Bucket.Default, streamId, streamRevision, payload)
        {}

        public Snapshot(string bucket, string streamId, int streamRevision, object payload)
            : this()
        {
            Bucket = bucket;
            StreamId = streamId;
            StreamVersion = streamRevision;
            Payload = payload;
        }
        protected Snapshot()
        { }

        public String Bucket { get; private set; }
        public String StreamId { get; private set; }
        public Int32 StreamVersion { get; private set; }
        public Object Payload { get; private set; }
    }
}