using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface ISnapshot
    {
        String Bucket { get; }

        String StreamId { get; }

        Int32 StreamVersion { get; }

        Object Payload { get; }
    }
}