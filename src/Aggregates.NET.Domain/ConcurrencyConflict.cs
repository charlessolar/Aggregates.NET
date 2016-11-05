using System;
using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus.ObjectBuilder;
using NServiceBus.Settings;

namespace Aggregates
{
    public enum ConcurrencyConflict
    {
        Ignore,
        Discard,
        ResolveStrongly,
        ResolveWeakly,
        Custom
    }

    public delegate IResolveConflicts ResolverBuilder(IBuilder builder, Type type);

    public class ConcurrencyStrategy : Enumeration<ConcurrencyStrategy, ConcurrencyConflict>
    {
        public static ConcurrencyStrategy Ignore = new ConcurrencyStrategy(ConcurrencyConflict.Ignore, "Ignore",
            (b, _) =>
            {
                var settings = b.Build<ReadOnlySettings>();
                return new IgnoreConflictResolver(b.Build<IStoreEvents>(),
                    settings.Get<StreamIdGenerator>("StreamGenerator"));
            });
        public static ConcurrencyStrategy Discard = new ConcurrencyStrategy(ConcurrencyConflict.Discard, "Discard", (b, _) => b.Build<DiscardConflictResolver>());
        public static ConcurrencyStrategy ResolveStrongly = new ConcurrencyStrategy(ConcurrencyConflict.ResolveStrongly, "ResolveStrongly", (b, _) => b.Build<ResolveStronglyConflictResolver>());
        public static ConcurrencyStrategy ResolveWeakly = new ConcurrencyStrategy(ConcurrencyConflict.ResolveWeakly, "ResolveWeakly", (b, _) => b.Build<ResolveWeaklyConflictResolver>());
        public static ConcurrencyStrategy Custom = new ConcurrencyStrategy(ConcurrencyConflict.Custom, "Custom", (b, type) => (IResolveConflicts)b.Build(type));

        public ConcurrencyStrategy(ConcurrencyConflict value, string displayName, ResolverBuilder builder) : base(value, displayName)
        {
            Build = builder;
        }

        public ResolverBuilder Build { get; private set; }
    }

}
