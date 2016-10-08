using System;

namespace Aggregates.Exceptions
{
    public class DuplicateCommitException : Exception
    {
        public DuplicateCommitException() { }
        public DuplicateCommitException(string message) : base(message) { }
        public DuplicateCommitException(string message, Exception innerException) : base(message, innerException) { }
    }
}
