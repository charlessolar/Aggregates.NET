using Aggregates.Exceptions;
using Aggregates.Extensions;
using NServiceBus.Logging;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Settings;
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
        private static readonly ILog Logger = LogManager.GetLogger(typeof(SafetyNet));
        private readonly Int32 _maxRetries;

        public SafetyNet(ReadOnlySettings settings)
        {
            _maxRetries = settings.Get<Int32>("MaxRetries");
        }
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
            } while (!success && retries < _maxRetries);
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
