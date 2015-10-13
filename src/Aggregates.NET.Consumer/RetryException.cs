using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates
{
    // When thrown from a handler, it will signal the dispatcher to continue processing the event and try the handler later
    // Used in case a handler depends on another handler
    public class RetryException : Exception
    {
    }
}
