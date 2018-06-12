using System;

namespace Aggregates.Exceptions
{
    public class PersistenceException : Exception
    {
        public PersistenceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}