using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class VersionException : Exception
    {
        public VersionException()
        {
        }
        public VersionException(String message) : base(message)
        {
        }
        public VersionException(String message, Exception innerException) : base(message, innerException)
        {
        }
    }
}