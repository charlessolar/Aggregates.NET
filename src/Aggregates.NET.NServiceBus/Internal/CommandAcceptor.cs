﻿using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Pipeline;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class CommandAcceptor : Behavior<IIncomingLogicalMessageContext>
    {
        private readonly ILogger Logger;

        private readonly IMetrics _metrics;

        public CommandAcceptor(ILogger<CommandAcceptor> logger, IMetrics metrics)
        {
            Logger = logger;
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
                        // if part of saga be sure to transfer that header
                        var replyOptions = new ReplyOptions();
                        if (context.MessageHeaders.TryGetValue(Defaults.SagaHeader, out var sagaId))
                            replyOptions.SetHeader(Defaults.SagaHeader, sagaId);

                        replyOptions.RequireImmediateDispatch();
                        // Tell the sender the command was accepted
                        var accept = context.Builder.GetService<Action<Accept>>();
                        await context.Reply<Accept>(accept, replyOptions).ConfigureAwait(false);
                    }
                }
                catch (BusinessException e)
                {
                    _metrics.Mark("Business Exceptions", Unit.Errors);

                    Logger.InfoEvent("BusinessException", "[{MessageId:l}] {MessageType} was rejected due to business exception: {Message}", context.MessageId, context.Message.MessageType.FullName, e.Message);

                    // so failure reply behavior doesnt send a reply as well
                    context.MessageHandled = true;

                    if (!context.MessageHeaders.ContainsKey(Defaults.RequestResponse) || context.MessageHeaders[Defaults.RequestResponse] != "1")
                        throw; // dont need a reply

                    // if part of saga be sure to transfer that header
                    var replyOptions = new ReplyOptions();
                    if (context.MessageHeaders.TryGetValue(Defaults.SagaHeader, out var sagaId))
                        replyOptions.SetHeader(Defaults.SagaHeader, sagaId);

                    replyOptions.RequireImmediateDispatch();
                    // Tell the sender the command was rejected due to a business exception
                    var rejection = context.Builder.GetService<Action<BusinessException, Reject>>();
                    await context.Reply<Reject>((msg) => rejection(e, msg), replyOptions).ConfigureAwait(false);

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
        public CommandAcceptorRegistration() : base(
            stepId: "CommandAcceptor",
            behavior: typeof(CommandAcceptor),
            description: "Filters [BusinessException] from processing failures",
            factoryMethod: (b) => new CommandAcceptor(b.GetService<ILogger<CommandAcceptor>>(), b.GetService<IMetrics>())
        )
        {
            // If a command fails business exception uow still needs to error out
            InsertBefore("UnitOfWorkExecution");
        }
    }
}
