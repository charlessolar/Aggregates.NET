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
using System.Diagnostics.CodeAnalysis;

namespace Aggregates.Internal
{
    public class CommandAcceptor : Behavior<IIncomingLogicalMessageContext>
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
                    
                    Logger.InfoEvent("BusinessException", "{MessageId} {MessageType} rejected {Message}", context.MessageId, context.Message.MessageType.FullName, e.Message);
                    if (!context.MessageHeaders.ContainsKey(Defaults.RequestResponse) || context.MessageHeaders[Defaults.RequestResponse] != "1")
                        return; // Dont throw, business exceptions are not message failures
                    
                    // Tell the sender the command was rejected due to a business exception
                    var rejection = context.Builder.Build<Func<BusinessException, Reject>>();
                    await context.Reply(rejection(e)).ConfigureAwait(false);

                    // ExceptionRejector will filter out BusinessException, throw is just to cancel the UnitOfWork
                    throw;
                }
                return;
            }

            await next().ConfigureAwait(false);
        }
    }
    [ExcludeFromCodeCoverage]
    internal class CommandAcceptorRegistration : RegisterStep
    {
        public CommandAcceptorRegistration(IContainer container) : base(
            stepId: "CommandAcceptor",
            behavior: typeof(CommandAcceptor),
            description: "Filters [BusinessException] from processing failures",
            factoryMethod: (b) => new CommandAcceptor(container.Resolve<IMetrics>())
        )
        {
            // If a command fails business exception uow still needs to error out
            InsertBefore("UnitOfWorkExecution");
        }
    }
}
