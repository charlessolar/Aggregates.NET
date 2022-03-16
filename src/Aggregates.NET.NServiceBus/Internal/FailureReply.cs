using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Messages;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Pipeline;
using NServiceBus.Transport;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class FailureReply : Behavior<IIncomingLogicalMessageContext>
    {
        private readonly ILogger Logger;

        private readonly IMetrics _metrics;

        public FailureReply(ILogger<FailureReply> logger, IMetrics metrics)
        {
            Logger = logger;
            _metrics = metrics;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            await next().ConfigureAwait(false);

            // Message is destined for error queue
            if (context.Headers.ContainsKey(NSBDefaults.FailedHeader))
            {
                // Special exception we dont want to retry or reply
                if (context.MessageHandled)
                    return;
                // Only send reply if the message is a SEND, else we risk endless reply loops as message failures bounce back and forth
                if (context.GetMessageIntent() != MessageIntentEnum.Send && context.GetMessageIntent() != MessageIntentEnum.Publish)
                    return;

                // At this point message is dead - should be moved to error queue, send message to client that their request was rejected due to error 
                _metrics.Mark("Message Faults", Unit.Errors);

                // Only need to reply if the client expects it
                if (!context.MessageHeaders.ContainsKey(Defaults.RequestResponse) || context.MessageHeaders[Defaults.RequestResponse] != "1")
                    return;

                var retried = "";
                context.Headers.TryGetValue(NSBDefaults.RetryHeader, out retried);

                // if part of saga be sure to transfer that header
                var replyOptions = new ReplyOptions();
                if (context.MessageHeaders.TryGetValue(Defaults.SagaHeader, out var sagaId))
                    replyOptions.SetHeader(Defaults.SagaHeader, sagaId);

                // reply must be sent immediately
                replyOptions.RequireImmediateDispatch();

                // Tell the sender the command was not handled due to a service exception
                var rejection = context.Builder.Build<Action<string, string, Error>>();
                context.Headers.TryGetValue(NSBDefaults.ExceptionTypeHeader, out var exceptionType);
                context.Headers.TryGetValue(NSBDefaults.ExceptionStack, out var exceptionStack);
                // Wrap exception in our object which is serializable
                await context.Reply<Error>((message) => rejection(exceptionType, exceptionStack, message), replyOptions)
                        .ConfigureAwait(false);
            }

        }
    }
    [ExcludeFromCodeCoverage]
    internal class FailureReplyRegistration : RegisterStep
    {
        public FailureReplyRegistration() : base(
            stepId: "FailureReply",
            behavior: typeof(FailureReply),
            description: "notifies client when the command fails",
            factoryMethod: (b) => new FailureReply(b.Build<ILogger<FailureReply>>(), b.Build<IMetrics>())
        )
        {
            InsertBefore("MutateIncomingMessages");
        }
    }
}
