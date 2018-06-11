using System;

namespace Aggregates
{
    /// <summary>
    /// Used by conflict resolvers to indicate that the resolution has failed and the command needs to be retried
    /// </summary>
    public class AbandonConflictException :Exception
    {
        public AbandonConflictException() : base("Conflict resolution was abandoned") { }
        public AbandonConflictException(string message) : base($"Conflict resolution was abandoned: {message}") { }
    }
}
