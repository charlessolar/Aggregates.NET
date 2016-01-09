using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class NotFoundException : Exception
    {
        public NotFoundException() { }
        public NotFoundException(String message) : base(message) { }
        public NotFoundException(String message, Exception innerException) : base(message, innerException) { }
    }
}
