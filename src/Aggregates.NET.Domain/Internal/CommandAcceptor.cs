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
    internal class CommandAcceptor : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CommandAcceptor));

        private static Meter _errorsMeter = Metric.Meter("Business Exceptions", Unit.Errors);        

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if (context.Message.Instance is ICommand)
            {
                try
                {
                    await next();

                    // Only need to reply if the client expects it
                    if (context.MessageHeaders.ContainsKey(Defaults.REQUEST_RESPONSE) && context.MessageHeaders[Defaults.REQUEST_RESPONSE] == "1")
                    {
                        // Tell the sender the command was accepted
                        var accept = context.Builder.Build<Func<Accept>>();
                        await context.Reply(accept());
                    }
                }
                catch (BusinessException e)
                {
                    if (!context.MessageHeaders.ContainsKey(Defaults.REQUEST_RESPONSE) || context.MessageHeaders[Defaults.REQUEST_RESPONSE] != "1")
                        return; // Dont throw, business exceptions are not message failures

                    _errorsMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Command {context.Message.MessageType.FullName} was rejected\nException: {e}");
                    // Tell the sender the command was rejected due to a business exception
                    var rejection = context.Builder.Build<Func<BusinessException, Reject>>();
                    await context.Reply(rejection(e));
                }
                return;
            }

            await next();
        }
    }
}
