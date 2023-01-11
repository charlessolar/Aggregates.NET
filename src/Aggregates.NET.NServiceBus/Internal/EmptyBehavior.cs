using NServiceBus.Pipeline;
using System;
using System.Diagnostics.CodeAnalysis;
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
