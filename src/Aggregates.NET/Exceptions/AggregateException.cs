using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class AggregateException : Exception
    {
        public AggregateException() { }
        public AggregateException(String message) : base(message) { }
        public AggregateException(String message, Exception innerException) : base(message, innerException) { }
    }
}
