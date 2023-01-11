using System;

namespace Aggregates.Exceptions
{
    public class NotFoundException : Exception
    {
        public NotFoundException() { }
        public NotFoundException(string stream, string client) : base($"Stream [{stream}] does not exist on [{client}]") { }
    }
}
