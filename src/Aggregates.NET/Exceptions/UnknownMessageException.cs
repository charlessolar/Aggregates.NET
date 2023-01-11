using System;

namespace Aggregates.Exceptions
{
    public class UnknownMessageException : Exception
    {
        public UnknownMessageException(string typeName) : base($"Message {typeName} is not known") { }
        public UnknownMessageException(Type type) : base($"Message {type.FullName} is not known") { }
    }
}
