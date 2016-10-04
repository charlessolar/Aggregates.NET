using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Exceptions
{
    public class NoRouteException : Exception
    {
        public NoRouteException(String Message) : base(Message) { }
    }
}
