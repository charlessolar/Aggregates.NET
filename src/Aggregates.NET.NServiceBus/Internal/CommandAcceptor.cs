using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Aggregates.Attributes;
using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus.MessageInterfaces;
using NServiceBus.ObjectBuilder;
using NServiceBus.Pipeline;
using Aggregates.Messages;
using NServiceBus;

namespace Aggregates.Internal
{
    internal class CommandAcceptor : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("CommandAcceptor");

        private readonly IMetrics _metrics;

        public CommandAcceptor(IMetrics metrics)
        {
            _metrics = metrics;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            if (context.Message.Instance is Messages.ICommand)
            {
                try
                {
                    await next().ConfigureAwait(false);

                    // Only need to reply if the client expects it
                    if (context.MessageHeaders.ContainsKey(Defaults.RequestResponse) && context.MessageHeaders[Defaults.RequestResponse] == "1")
                    {
                        // Tell the sender the command was accepted
                        var accept = context.Builder.Build<Func<Accept>>();
                        await context.Reply(accept()).ConfigureAwait(false);
                    }
                }
                catch (BusinessException e)
                {
                    _metrics.Mark("Business Exceptions", Unit.Errors);

                    Logger.Write(LogLevel.Info, () => $"Caught business exception: {e.Message}");
                    if (!context.MessageHeaders.ContainsKey(Defaults.RequestResponse) || context.MessageHeaders[Defaults.RequestResponse] != "1")
                        return; // Dont throw, business exceptions are not message failures

                    Logger.Write(LogLevel.Debug, () => $"Command {context.Message.MessageType.FullName} was rejected\nException: {e.Message}");
                    // Tell the sender the command was rejected due to a business exception
                    var rejection = context.Builder.Build<Func<BusinessException, Reject>>();
                    await context.Reply(rejection(e)).ConfigureAwait(false);
                }
                return;
            }

            await next().ConfigureAwait(false);
        }
    }
    internal class CommandAcceptorRegistration : RegisterStep
    {
        public CommandAcceptorRegistration() : base(
            stepId: "CommandAcceptor",
            behavior: typeof(CommandAcceptor),
            description: "Filters [BusinessException] from processing failures"
        )
        {
            InsertBefore("UnitOfWorkExecution");
        }
    }
}
