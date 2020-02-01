using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using Aggregates.Messages;
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
    public class ExceptionRejector : Behavior<IIncomingLogicalMessageContext>
    {
        private static readonly ConcurrentDictionary<string, int> RetryRegistry = new ConcurrentDictionary<string, int>();
        private static readonly ILog Logger = LogProvider.GetLogger("ExceptionRejector");

        private readonly IMetrics _metrics;
        private readonly DelayedRetry _retry;
        private readonly IMessageSerializer _serializer;
        private readonly int _retries;

        public ExceptionRejector(IMetrics metrics, IMessageSerializer serializer, DelayedRetry retry)
        {
            _metrics = metrics;
            _serializer = serializer;
            _retry = retry;

            _retries = Configuration.Settings.Retries;
        }

        public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
        {
            var messageId = context.MessageId;
            var retries = 0;

            try
            {
                RetryRegistry.TryRemove(messageId, out retries);
                context.Headers[Defaults.Retries] = retries.ToString();
                context.Extensions.Set(Defaults.Retries, retries);

                await next().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Special exception we dont want to retry or reply
                if (e is BusinessException || context.MessageHandled)
                    return;
                
                if (retries < _retries || _retries == -1)
                {
                    Logger.LogEvent((retries > _retries / 2) ? LogLevel.Warn : LogLevel.Info, "Catch", e, "[{MessageId:l}] will retry {Retries}/{MaxRetries}: {ExceptionType} - {ExceptionMessage}", messageId,
                        retries, _retries, e.GetType().Name, e.Message);

                    RetryRegistry.TryAdd(messageId, retries + 1);

                    var message = new FullMessage
                    {
                        Message = context.Message.Instance as Messages.IMessage,
                        Headers = context.MessageHeaders.ToDictionary(kv => kv.Key, kv => kv.Value)
                    };

                    // todo: Not ideal for this to be here -
                    // but unpacking these headers takes place further down the queue after UOW start
                    // I don't want to start a new unit of work for each message in a bulk message (defeats the purpose)
                    // So need to do something a little special here
                    if (context.Extensions.TryGet(Defaults.BulkHeader, out IFullMessage[] delayedMessages))
                        message.Message = new BulkMessage { Messages = delayedMessages };
                    if (context.Extensions.TryGet(Defaults.LocalHeader, out object local))
                        message.Message = local as Messages.IMessage;

                    _retry.QueueRetry(message, TimeSpan.FromMilliseconds(500));
                    // retry out of the pipeline so NSB can continue processing other messages & we can delay
                    //throw;
                    return;
                }

                // at this point the message has failed, so a THROW will move it to the error queue

                // Only send reply if the message is a SEND, else we risk endless reply loops as message failures bounce back and forth
                if (context.GetMessageIntent() != MessageIntentEnum.Send && context.GetMessageIntent() != MessageIntentEnum.Publish)
                    return;

                // At this point message is dead - should be moved to error queue, send message to client that their request was rejected due to error 
                _metrics.Mark("Message Faults", Unit.Errors);

                Logger.ErrorEvent("Fault", e, "[{MessageId:l}] has failed {Retries} times\n{@Headers}\n{ExceptionType} - {ExceptionMessage}", messageId, retries, context.MessageHeaders, e.GetType().Name, e.Message);
                // Only need to reply if the client expects it
                if (!context.MessageHeaders.ContainsKey(Defaults.RequestResponse) || context.MessageHeaders[Defaults.RequestResponse] != "1")
                    throw;

                // if part of saga be sure to transfer that header
                var replyOptions = new ReplyOptions();
                if (context.MessageHeaders.TryGetValue(Defaults.SagaHeader, out var sagaId))
                    replyOptions.SetHeader(Defaults.SagaHeader, sagaId);

                // Tell the sender the command was not handled due to a service exception
                var rejection = context.Builder.Build<Action<Exception, string, Error>>();
                // Wrap exception in our object which is serializable
                await context.Reply<Error>((message) => rejection(e,
                            $"Rejected message after {retries} attempts!", message), replyOptions)
                        .ConfigureAwait(false);

                // Should be the last throw for this message - if RecoveryPolicy is properly set the message will be sent over to error queue
                throw;

            }
        }
    }
    [ExcludeFromCodeCoverage]
    internal class ExceptionRejectorRegistration : RegisterStep
    {
        public ExceptionRejectorRegistration() : base(
            stepId: "ExceptionRejector",
            behavior: typeof(ExceptionRejector),
            description: "handles exceptions and retries",
            factoryMethod: (b) => new ExceptionRejector(b.Build<IMetrics>(), b.Build<IMessageSerializer>(), b.Build<DelayedRetry>())
        )
        {
            InsertBefore("MutateIncomingMessages");
        }
    }
}
