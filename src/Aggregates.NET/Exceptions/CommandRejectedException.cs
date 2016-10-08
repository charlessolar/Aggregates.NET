using System;

namespace Aggregates.Exceptions
{
    public class CommandRejectedException : Exception
    {
        public CommandRejectedException()
        {
        }

        public CommandRejectedException(string message)
            : base(message)
        {
        }

        public CommandRejectedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
