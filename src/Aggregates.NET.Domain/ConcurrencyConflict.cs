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
}
