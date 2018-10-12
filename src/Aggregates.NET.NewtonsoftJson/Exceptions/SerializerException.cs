using System;
using System.Collections.Generic;
using System.Text;

namespace Aggregates.Exceptions
{
    public class SerializerException : Exception
    {
        public SerializerException(Exception inner, string path) : base($"Serialization exception on '{path}'", inner) { }
    }
}
