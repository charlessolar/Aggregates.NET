using System;

namespace Aggregates.Exceptions
{
    public class NoRouteException : Exception
    {
        public NoRouteException() : base("Unknown route") { }
        public NoRouteException(string message) : base(message) { }
    }
}
