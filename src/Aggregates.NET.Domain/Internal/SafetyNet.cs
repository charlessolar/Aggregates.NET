using Aggregates.Exceptions;
using Aggregates.Extensions;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    internal class SafetyNet : IBehavior<IncomingContext>
    {

        public void Invoke(IncomingContext context, Action next)
        {
            // Catch all our internal exceptions, retrying the command up to 5 times before giving up
            var retries = 0;
            bool success = false;
            do
            {
                try
                {
                    next();
                    success = true;
                }
                catch (NotFoundException) { }
                catch (PersistenceException) { }
                catch (AggregateException) { }
                catch (ConflictingCommandException) { }
                if (!success)
                {
                    retries++;
                    Thread.Sleep(50);
                }
            } while (!success && retries < 5);
        }
    }

    internal class SafetyNetRegistration : RegisterStep
    {
        public SafetyNetRegistration()
            : base("SafetyNet", typeof(SafetyNet), "Inserts a safety net into the chain to catch Aggregates.NET exceptions for retrying")
        {
            InsertBefore(WellKnownStep.ExecuteUnitOfWork);
        }
    }
}
