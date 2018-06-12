using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Exceptions
{
    public class RejectedException : Exception
    {
        public RejectedException(Type command, string message) :
            base($"Command {command.FullName} was rejected: {message}")
        { }

        public RejectedException(Type command, string message, Exception innerException) :
            base($"Command {command.FullName} was rejected: {message}", innerException)
        { }
    }
}
