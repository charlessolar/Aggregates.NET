using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class ConflictingCommandException : System.AggregateException
    {
        public ConflictingCommandException() { }
        public ConflictingCommandException(String message) : base(message) { }
        public ConflictingCommandException(String message, Exception innerException, Exception resolve) : base(message, innerException, resolve) { }
    }
}
