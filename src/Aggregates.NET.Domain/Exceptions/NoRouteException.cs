using System;

namespace Aggregates.Exceptions
{
    public class NoRouteException : Exception
    {
        public NoRouteException(string message) : base(message) { }
    }
}
