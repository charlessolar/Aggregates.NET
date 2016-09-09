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
                BusinessException exception = null;
                try
                {
                    next();
                    // Tell the sender the command was accepted
                    var accept = context.Builder.Build<Func<Accept>>();
                    _bus.Reply(accept());
                }
                catch (System.AggregateException e)
                {
                    if (!(e is BusinessException) && !(e.InnerException is BusinessException) && !e.InnerExceptions.OfType<BusinessException>().Any())
                        throw;

                    if (e is BusinessException)
                        exception = e as BusinessException;
                    else if (e.InnerException is BusinessException)
                        exception = e.InnerException as BusinessException;
                    else
                        exception = new BusinessException(e.Message, e.InnerExceptions.OfType<BusinessException>());
                }
                if (exception != null)
                {
                    _errorsMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Command {context.IncomingLogicalMessage.MessageType.FullName} was rejected\nException: {exception}");
                    // Tell the sender the command was rejected due to a business exception
                    var rejection = context.Builder.Build<Func<BusinessException, Reject>>();
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
