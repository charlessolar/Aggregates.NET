using System;

namespace Aggregates.Exceptions
{
    public class ConflictResolutionFailedException : Exception
    {
        public ConflictResolutionFailedException() { }
        public ConflictResolutionFailedException(string message) : base(message) { }
        public ConflictResolutionFailedException(string message, Exception innerException) : base(message, innerException) { }
    }
}
