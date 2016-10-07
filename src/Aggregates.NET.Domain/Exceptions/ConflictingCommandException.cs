using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class ConflictResolutionFailedException : System.Exception
    {
        public ConflictResolutionFailedException() { }
        public ConflictResolutionFailedException(String message) : base(message) { }
        public ConflictResolutionFailedException(String message, Exception innerException) : base(message, innerException) { }
    }
}
