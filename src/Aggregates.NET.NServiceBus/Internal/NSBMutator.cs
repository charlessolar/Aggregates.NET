using Aggregates.Contracts;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    class NSBMutator : IMutate
    {
        public IMutating MutateIncoming(IMutating mutating)
        {
            // Set aggregates.net message and corr id
            if (mutating.Headers.ContainsKey(Headers.MessageId))
                mutating.Headers[$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"] = mutating.Headers[Headers.MessageId];
            if (mutating.Headers.ContainsKey(Headers.CorrelationId))
                mutating.Headers[$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"] = mutating.Headers[Headers.CorrelationId];

            return mutating;
        }

        public IMutating MutateOutgoing(IMutating mutating)
        {
            // Set aggregates.net message and corr id
            if (mutating.Headers.ContainsKey(Headers.MessageId))
                mutating.Headers[$"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}"] = mutating.Headers[Headers.MessageId];
            if (mutating.Headers.ContainsKey(Headers.CorrelationId))
                mutating.Headers[$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"] = mutating.Headers[Headers.CorrelationId];

            return mutating;
        }
    }
}
