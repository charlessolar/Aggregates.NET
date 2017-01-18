using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class EndpointVersionException : Exception
    {
        public EndpointVersionException(string message) : base(message)
        {
        }
    }
}
