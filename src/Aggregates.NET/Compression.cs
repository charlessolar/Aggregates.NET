using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates
{
    [Flags]
    public enum Compression
    {
        None = 0x0,
        Events = 0x01,
        Snapshots = 0x10,
        All = 0x11,
    }
}
