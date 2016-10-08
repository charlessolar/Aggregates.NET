using System;

namespace Aggregates.Exceptions
{
    /// <summary>
    /// Used by conflict resolvers to indicate that the resolution has failed and the command needs to be retried
    /// </summary>
    public class AbandonConflictException :Exception
    {
        public AbandonConflictException() { }
        public AbandonConflictException(string message) : base(message) { }
    }
}
