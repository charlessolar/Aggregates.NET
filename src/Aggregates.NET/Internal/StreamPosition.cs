using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    static class StreamPosition
    {
        //
        // Summary:
        //     The last event in the stream.
        public const long End = -1;
        //
        // Summary:
        //     The first event in a stream
        public const long Start = 0;
    }
}
