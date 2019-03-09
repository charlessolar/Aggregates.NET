using Aggregates.Extensions;
using System;

namespace Aggregates.Exceptions
{
    public class ConflictResolutionFailedException : Exception
    {
        public ConflictResolutionFailedException(Type entityType, string bucket, Id entityId, Id[] parents) 
            : base($"Failed to resolve conflicts on entity [{entityType.FullName}] bucket [{bucket}] id [{entityId}] parents [{parents.Flatten()}]") { }
        public ConflictResolutionFailedException(Type entityType, string bucket, Id entityId, Id[] parents, string message) 
            : base($"Failed to resolve conflicts on entity [{entityType.FullName}] bucket [{bucket}] id [{entityId}] parents [{parents.Flatten()}] for reason: {message}") { }
        public ConflictResolutionFailedException(Type entityType, string bucket, Id entityId, Id[] parents, string message, Exception innerException) 
            : base($"Failed to resolve conflicts on entity [{entityType.FullName}] bucket [{bucket}] id [{entityId}] parents [{parents.Flatten()}] for reason: {message}", innerException) { }
    }
}
