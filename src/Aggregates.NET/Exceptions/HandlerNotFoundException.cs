using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    public class HandlerNotFoundException : Exception
    {
        public HandlerNotFoundException() { }
        public HandlerNotFoundException(String message) : base(message) { }
        public HandlerNotFoundException(String message, Exception innerException) : base(message, innerException) { }
    }
}
