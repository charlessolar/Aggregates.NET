using System;

namespace Aggregates.Exceptions
{
    public class SerializationException : Exception
    {
        public SerializationException(string message, Exception inner) : base(message, inner) { }
    }
}
