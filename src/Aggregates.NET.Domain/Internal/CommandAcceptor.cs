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
        private readonly IBus _bus;

        public CommandAcceptor(IBus bus)
        {
            _bus = bus;
        }

        public void Invoke(IncomingContext context, Action next)
        {
            if (context.IncomingLogicalMessage.Instance is ICommand)
            {
                System.Exception exception = null;
                try
                {
                    next();
                    // Tell the sender the command was accepted
                    var accept = context.Builder.Build<Func<Accept>>();
                    _bus.Reply(accept());
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
                    Logger.DebugFormat("Command {0} was rejected\nException: {1}", context.IncomingLogicalMessage.MessageType.FullName, exception);
                    // Tell the sender the command was rejected due to a business exception
                    var rejection = context.Builder.Build<Func<Exception, Reject>>();
                    _bus.Reply(rejection(exception));
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
