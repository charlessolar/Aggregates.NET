using Aggregates.Contracts;
using Aggregates.Internal;
using NServiceBus.ObjectBuilder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
