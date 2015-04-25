using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface ISnapshot
    {
        String BucketId { get; }

        String StreamId { get; }

        Int32 StreamRevision { get; }

        Object Payload { get; }
    }
}
