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
    internal class CommandAcceptor : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CommandAcceptor));

        private static Meter _errorsMeter = Metric.Meter("Business Exceptions", Unit.Errors);

        public IBus _bus;
        public Func<Accept> Acceptance;
        public Func<Exception, Reject> Rejection;
        

        public void Invoke(IncomingContext context, Action next)
        {
            if (context.IncomingLogicalMessage.Instance is ICommand)
            {
                System.Exception exception = null;
                try
                {
                    next();
                    // Tell the sender the command was accepted
                    _bus.Reply(Acceptance());
                }
                catch (System.AggregateException e)
                {
                    if (!(e.InnerException is BusinessException) && !e.InnerExceptions.Any(x => x is BusinessException))
                        throw;

                    exception = e;
                }
                catch (BusinessException e)
                {
                    exception = e;
                }
                if (exception != null)
                {
                    _errorsMeter.Mark();
                    Logger.InfoFormat("Command {0} was rejected\nException: {1}", context.IncomingLogicalMessage.MessageType.FullName, exception);
                    // Tell the sender the command was rejected due to a business exception
                    _bus.Reply(Rejection(exception));
                }
                return;

            }

            next();
        }
    }

    internal class CommandAcceptorRegistration : RegisterStep
    {
        public CommandAcceptorRegistration()
            : base("CommandAcceptor", typeof(CommandAcceptor), "Filters [BusinessException] from processing failures")
        {
            InsertBefore(WellKnownStep.LoadHandlers);

        }
    }
}
