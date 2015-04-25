using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class Snapshot : ISnapshot
    {
        public Snapshot(string streamId, int streamRevision, object payload)
               : this(Bucket.Default, streamId, streamRevision, payload)
        { }

        public Snapshot(string bucketId, string streamId, int streamRevision, object payload)
            : this()
        {
            BucketId = bucketId;
            StreamId = streamId;
            StreamRevision = streamRevision;
            Payload = payload;
        }
        protected Snapshot()
        { }

        public String BucketId { get; private set; }
        public String StreamId { get; private set; }
        public Int32 StreamRevision { get; private set; }
        public Object Payload { get; private set; }
    }
}