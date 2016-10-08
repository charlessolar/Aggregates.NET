using System;

namespace Aggregates.Exceptions
{
    public class AggregateException : Exception
    {
        public AggregateException() { }
        public AggregateException(string message) : base(message) { }
        public AggregateException(string message, Exception innerException) : base(message, innerException) { }
    }
}
