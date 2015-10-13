using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class DispatchException : Exception
    {
        public DispatchException() { }
        public DispatchException(String message) : base(message) { }
        public DispatchException(String message, Exception innerException) : base(message, innerException) { }
    }
}
