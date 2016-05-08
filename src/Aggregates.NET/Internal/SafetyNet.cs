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
    public class SafetyNet : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(SafetyNet));
        private readonly Int32 _maxRetries;
        
        // NSB doesn't allow try/catch next repeating calls, the pipeline gets all messed up

        public SafetyNet(ReadOnlySettings settings)
        {
            _maxRetries = settings.Get<Int32>("MaxRetries");
        }
        public void Invoke(IncomingContext context, Action next)
        {

            // Catch all our internal exceptions, retrying the command up to 5 times before giving up
            var retries = 0;
            var exceptions = new List<Exception>();
            bool success = false;
            do
            {
                Exception exception = null;
                try
                {
                    next();
                    success = true;
                }
                catch (System.AggregateException e)
                {
                    if (!(e.InnerException is NotFoundException || e.InnerException is PersistenceException || e.InnerException is AggregateException || e.InnerException is ConflictingCommandException) && 
                            !e.InnerExceptions.Any(x => x is NotFoundException || x is PersistenceException || x is AggregateException || x is ConflictingCommandException))
                        throw;
                    exception = e;
                }
                catch (NotFoundException e) { exception = e; }
                catch (PersistenceException e) { exception = e; }
                catch (AggregateException e) { exception = e; }
                catch (ConflictingCommandException e) { exception = e; }
                if (!success)
                {
                    exceptions.Add(exception);
                    retries++;
                    if (_maxRetries == -1 || retries > (_maxRetries / 2))
                        Logger.InfoFormat("Caught exception - retry {0}/{1}\nException: {2}", retries, _maxRetries, exception);
                    else
                        Logger.DebugFormat("Caught exception - retry {0}/{1}\nException: {2}", retries, _maxRetries, exception);
                    Thread.Sleep(50);
                }
            } while (!success && (_maxRetries == -1 || retries < _maxRetries));

            if (!success)
            {
                throw new System.AggregateException(exceptions);
            }
        }
    }

    public class SafetyNetRegistration : RegisterStep
    {
        public SafetyNetRegistration()
            : base("SafetyNet", typeof(SafetyNet), "Inserts a safety net into the chain to catch Aggregates.NET exceptions for retrying")
        {

            InsertBefore(WellKnownStep.ExecuteUnitOfWork);
        }
    }
}
