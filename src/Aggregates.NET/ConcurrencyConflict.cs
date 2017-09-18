using System;
using Aggregates.Contracts;
using Aggregates.Internal;

namespace Aggregates
{
    public enum ConcurrencyConflict
    {
        Throw,
        Ignore,
        Discard,
        ResolveStrongly,
        ResolveWeakly,
        Custom
    }
}
