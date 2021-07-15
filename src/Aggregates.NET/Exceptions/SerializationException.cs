using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Exceptions
{
    public class SerializationException : Exception
    {
        public SerializationException(string message, Exception inner):base(message, inner) { }
    }
}
