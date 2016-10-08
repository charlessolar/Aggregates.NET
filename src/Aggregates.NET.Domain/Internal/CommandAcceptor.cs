using System;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Messages;
using Metrics;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class CommandAcceptor : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(CommandAcceptor));

        private static readonly Meter ErrorsMeter = Metric.Meter("Business Exceptions", Unit.Errors);        

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if (context.Message.Instance is ICommand)
            {
                try
                {
                    await next().ConfigureAwait(false);

                    // Only need to reply if the client expects it
                    if (context.MessageHeaders.ContainsKey(Defaults.RequestResponse) && context.MessageHeaders[Defaults.RequestResponse] == "1")
                    {
                        // Tell the sender the command was accepted
                        var accept = context.Builder.Build<Func<IAccept>>();
                        await context.Reply(accept()).ConfigureAwait(false);
                    }
                }
                catch (BusinessException e)
                {
                    Logger.Write(LogLevel.Info, () => $"Caught business exception: {e.Message}");
                    if (!context.MessageHeaders.ContainsKey(Defaults.RequestResponse) || context.MessageHeaders[Defaults.RequestResponse] != "1")
                        return; // Dont throw, business exceptions are not message failures

                    ErrorsMeter.Mark();
                    Logger.Write(LogLevel.Debug, () => $"Command {context.Message.MessageType.FullName} was rejected\nException: {e.Message}");
                    // Tell the sender the command was rejected due to a business exception
                    var rejection = context.Builder.Build<Func<BusinessException, IReject>>();
                    await context.Reply(rejection(e)).ConfigureAwait(false);
                }
                return;
            }

            await next().ConfigureAwait(false);
        }
    }
}
