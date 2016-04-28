using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using NServiceBus.Pipeline.Contexts;
using NServiceBus.Logging;
using Metrics;
using Aggregates.Extensions;

namespace Aggregates.Internal
{
    internal class ExceptionRejector : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ExceptionRejector));

        private static Meter _errorsMeter = Metric.Meter("Message Faults", Unit.Errors);
        private readonly IBus _bus;

        public ExceptionRejector(IBus bus)
        {
            _bus = bus;
        }

        public void Invoke(IncomingContext context, Action next)
        {
            try
            {
                next();
            }
            catch (Exception e)
            {
                _errorsMeter.Mark();
                try
                {
                    Logger.WarnFormat("Message {0} has faulted!\nException: {1}", context.IncomingLogicalMessage.MessageType.FullName, e);
                }
                catch (KeyNotFoundException)
                {
                    Logger.WarnFormat("Message [Unknown] has faulted!\nException: {0}", e);
                }
                // Tell the sender the command was not handled due to a service exception
                var rejection = context.Builder.Build<Func<Exception, Error>>();
                _bus.Reply(rejection(e));
            }

        }
    }

    internal class ExceptionRejectorRegistration : RegisterStep
    {
        public ExceptionRejectorRegistration()
            : base("ExceptionRejector", typeof(ExceptionRejector), "Catches exceptions thrown while processing and reports to client via IReject")
        {
            InsertBefore(WellKnownStep.CreateChildContainer);

        }
    }
}
