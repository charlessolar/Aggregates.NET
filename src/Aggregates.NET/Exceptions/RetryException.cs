using System;

namespace Aggregates.Exceptions
{
    // When thrown from a handler, it will signal the dispatcher to continue processing the event and try the handler later
    // Used in case a handler depends on another handler
    public class RetryException : Exception
    {
        public RetryException() { }
        public RetryException(string message) : base(message) { }
    }
}
