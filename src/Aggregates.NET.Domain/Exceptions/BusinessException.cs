using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class BusinessException : Exception
    {
        public BusinessException() { }
        public BusinessException(String message) : base(message) { }
        public BusinessException(String message, Exception innerException) : base(message, innerException) { }
    }
}
