using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Pipeline;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    // used to replace behaviors we dont need in NSB
    [ExcludeFromCodeCoverage]
    internal class EmptyBehavior : Behavior<IIncomingPhysicalMessageContext>
    {
        public override Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            return next();
        }
    }
}
