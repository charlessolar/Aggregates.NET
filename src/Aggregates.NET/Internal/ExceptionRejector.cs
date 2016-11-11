using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Messages;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;
using NServiceBus.Transport;

namespace Aggregates.Internal
{
    internal class ExceptionRejector : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ExceptionRejector));
        
        private static readonly Meter ErrorsMeter = Metric.Meter("Message Faults", Unit.Errors);
        private readonly int _retries;

        public ExceptionRejector(int retries)
        {
            _retries = retries;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var retries = 0;
            while (retries <= _retries || _retries == -1)
            {
                try
                {
                    context.Extensions.Set(Defaults.Retries, retries);

                    await next().ConfigureAwait(false);
                    break;
                }
                catch (Exception e)
                {
                    if (retries < _retries || _retries == -1)
                    {
                        Logger.WriteFormat(LogLevel.Warn,
                            $"Message {context.MessageId} has faulted! {retries}/{_retries} times\nException: {e.GetType().FullName}\nHeaders: {JsonConvert.SerializeObject(context.MessageHeaders, Formatting.None)}\nBody: {Encoding.UTF8.GetString(context.Message.Body)}");

                        // do an immediate retry first time
                        await Task.Delay(100 * (retries / 2)).ConfigureAwait(false);
                        retries++;
                        continue;
                    }

                    // At this point message is dead - should be moved to error queue, send message to client that their request was rejected due to error 
                    ErrorsMeter.Mark();
                    
                    // Only send reply if the message is a SEND, else we risk endless reply loops as message failures bounce back and forth
                    if (context.Message.GetMesssageIntent() != MessageIntentEnum.Send || context.Message.GetMesssageIntent() == MessageIntentEnum.Reply) return;

                    Logger.WriteFormat(LogLevel.Warn,
                        $"Message {context.MessageId} has failed after {retries} attempts!\nException: {e.GetType().FullName}\nHeaders: {JsonConvert.SerializeObject(context.MessageHeaders, Formatting.None)}\nBody: {Encoding.UTF8.GetString(context.Message.Body)}");

                    // Only need to reply if the client expects it
                    if (!context.Message.Headers.ContainsKey(Defaults.RequestResponse) ||
                        context.Message.Headers[Defaults.RequestResponse] != "1")
                        throw;

                    // Tell the sender the command was not handled due to a service exception
                    var rejection = context.Builder.Build<Func<Exception, string, Error>>();
                    // Wrap exception in our object which is serializable
                    await
                        context.Reply(rejection(e,
                                $"Rejected message after {retries} attempts!\nPayload: {Encoding.UTF8.GetString(context.Message.Body)}"))
                            .ConfigureAwait(false);

                    // Should be the last throw for this message - if RecoveryPolicy is properly set the message will be sent over to error queue
                    throw;
                }
            }
        }
    }
}
