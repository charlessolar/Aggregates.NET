using System;

namespace Aggregates.Exceptions
{
    public class VersionException : Exception
    {
        public VersionException(String message) : base(message)
        {
        }

        public VersionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}