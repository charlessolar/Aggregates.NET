using Aggregates.Exceptions;
using Aggregates.Commands;
using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Integrations
{
    class ExceptionFilter : IBehavior<IncomingContext>
    {
        private readonly IBus _bus;
        public ExceptionFilter(IBus bus) { _bus = bus; }

        public void Invoke(IncomingContext context, Action next)
        {
            try
            {
                next();

                // Tell the sender the command was accepted
                _bus.Accept();
            }
            catch (BusinessException e)
            {
                // Tell the sender the command was rejected due to a business exception
                _bus.Reject(e.Message);
                // Don't throw exception to NServicebus because we don't wish to retry this command
            }
        }
    }

    class ExceptionFilterRegistration : RegisterStep
    {
        public ExceptionFilterRegistration()
            : base("ExceptionFilter", typeof(ExceptionFilter), "Filters [BusinessException] from processing failures")
        {
            InsertBefore(WellKnownStep.InvokeHandlers);
        }
    }
}
