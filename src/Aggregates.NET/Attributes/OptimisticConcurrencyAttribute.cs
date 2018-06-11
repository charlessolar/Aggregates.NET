using System;
using System.Collections.Generic;
using System.Text;
using Aggregates.Internal;

namespace Aggregates.Attributes
{
    /// <summary>
    /// Used to indicate entity specific concurrency exception handling
    /// By default all streams are resolved strongly which is to say if theres a conflict with the store processing is paused, we go get the latest version and run the entity's conflict handlers
    /// But for certain specific instances it may make more sense to just discard, ignore, or lazily resolve these conflicts - see docs
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class OptimisticConcurrencyAttribute : Attribute
    {
        public OptimisticConcurrencyAttribute(ConcurrencyConflict conflict = ConcurrencyConflict.ResolveStrongly, int resolveRetries = -1, Type resolver = null)
        {
            Conflict = ConcurrencyStrategy.FromValue(conflict);
            ResolveRetries = resolveRetries;
            Resolver = resolver;

            if (conflict == ConcurrencyConflict.Custom && resolver == null)
                throw new ArgumentException("For CUSTOM conflict resolution the Resolver parameter is required");
            if (resolver != null && !typeof(IResolveConflicts).IsAssignableFrom(resolver))
                throw new ArgumentException("Conflict resolver must inherit from IResolveConflicts");
        }

        internal ConcurrencyStrategy Conflict { get; private set; }
        public int? ResolveRetries { get; private set; }
        public Type Resolver { get; private set; }
    }
}
