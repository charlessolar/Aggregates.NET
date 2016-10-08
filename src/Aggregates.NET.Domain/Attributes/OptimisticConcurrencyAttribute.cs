using Aggregates.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Attributes
{
    /// <summary>
    /// Used to indicate entity specific concurrency exception handling
    /// By default all streams are resolved strongly which is to say if theres a conflict with the store processing is paused, we go get the latest version and run the entity's conflict handlers
    /// But for certain specific instances it may make more sense to just discard, ignore, or lazily resolve these conflicts - see docs
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class OptimisticConcurrencyAttribute : Attribute
    {
        public OptimisticConcurrencyAttribute(ConcurrencyConflict Conflict = ConcurrencyConflict.ResolveStrongly, Int32 ResolveRetries = -1, Type Resolver = null)
        {
            this.Conflict = ConcurrencyStrategy.FromValue(Conflict);
            this.ResolveRetries = ResolveRetries;
            this.Resolver = Resolver;

            if (Conflict == ConcurrencyConflict.Custom && Resolver == null)
                throw new ArgumentException("For CUSTOM conflict resolution the Resolver parameter is required");
            if (Resolver != null && typeof(IResolveConflicts).IsAssignableFrom(Resolver))
                throw new ArgumentException("Conflict resolver must inherit from IResolveConflicts");
        }

        public ConcurrencyStrategy Conflict { get; private set; }
        public Int32? ResolveRetries { get; private set; }
        public Type Resolver { get; private set; }
    }
}
