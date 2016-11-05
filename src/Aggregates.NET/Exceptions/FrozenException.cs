using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class FrozenException : VersionException
    {
        public FrozenException() : base("Stream is frozen") { }
    }
}
