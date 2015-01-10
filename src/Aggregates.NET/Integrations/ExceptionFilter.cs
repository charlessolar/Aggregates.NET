using Aggregates.Exceptions;
using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NServiceBus.ObjectBuilder;
using Aggregates.Messages;

namespace Aggregates.Integrations
{
    class ExceptionFilter : IBehavior<IncomingContext>
    {
        private readonly IBus _bus;
        private readonly IBuilder _builder;
        public ExceptionFilter(IBus bus, IBuilder builder) { 
            _bus = bus;
            _builder = builder;
        }

        public void Invoke(IncomingContext context, Action next)
        {
            try
            {
                next();

                // Tell the sender the command was accepted
                var acceptance = _builder.Build<Func<IAccept>>();
                _bus.Reply(acceptance());
            }
            catch (BusinessException e)
            {
                // Tell the sender the command was rejected due to a business exception
                var rejection = _builder.Build<Func<String, IReject>>();
                _bus.Reply(rejection(e.Message));
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
