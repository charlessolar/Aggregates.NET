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
using Newtonsoft.Json;
using NServiceBus.Settings;
using System.Threading;
using Aggregates.Contracts;
using System.Collections.Concurrent;

namespace Aggregates.Internal
{
    internal class ExceptionRejector : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ExceptionRejector));

        private static ConcurrentDictionary<String, Int32> _retryRegistry = new ConcurrentDictionary<String, Int32>();
        private static Meter _errorsMeter = Metric.Meter("Message Faults", Unit.Errors);
        private readonly Int32 _immediate;
        private readonly Boolean _forever;

        public ExceptionRejector(Int32 ImmediateRetries, Boolean Forever)
        {
            _immediate = ImmediateRetries;
            _forever = Forever;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var messageId = context.MessageId;
            Int32 existingRetry = 0;
            try
            {
                _retryRegistry.TryRemove(messageId, out existingRetry);
                context.Extensions.Set(Defaults.RETRIES, existingRetry);

                await next().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (existingRetry < _immediate)
                {
                    Logger.WriteFormat(LogLevel.Warn, $"Message {context.MessageId} has faulted! {existingRetry}/{_immediate} times\nException: {e.GetType().FullName}\nHeaders: {JsonConvert.SerializeObject(context.MessageHeaders, Formatting.None)}\nBody: {Encoding.UTF8.GetString(context.Message.Body)}");
                    _retryRegistry.TryAdd(messageId, existingRetry + 1);
                    throw;
                }
                // At this point message is dead - should be moved to error queue, send message to client that their request was rejected due to error 
                _errorsMeter.Mark();

                // They've chosen to never move to error queue so don't reply with error
                if (_forever)
                    throw;

                // Only send reply if the message is a SEND, else we risk endless reply loops as message failures bounce back and forth
                var intent = (MessageIntentEnum)Enum.Parse(typeof(MessageIntentEnum), context.Message.Headers[Headers.MessageIntent], true);
                if (intent != MessageIntentEnum.Send) return;

                // Tell the sender the command was not handled due to a service exception
                var rejection = context.Builder.Build<Func<Exception, String, Error>>();

                Logger.WriteFormat(LogLevel.Warn, $"Message {context.MessageId} has failed after {existingRetry} retries!\nException: {e.GetType().FullName}\nHeaders: {JsonConvert.SerializeObject(context.MessageHeaders, Formatting.None)}\nBody: {Encoding.UTF8.GetString(context.Message.Body)}");
                
                // Only need to reply if the client expects it
                if (!context.Message.Headers.ContainsKey(Defaults.REQUEST_RESPONSE) || context.Message.Headers[Defaults.REQUEST_RESPONSE] != "1")
                    throw;

                // Wrap exception in our object which is serializable
                await context.Reply(rejection(e, $"Rejected message after {existingRetry} retries!\n Payload: {Encoding.UTF8.GetString(context.Message.Body)}")).ConfigureAwait(false);

                // Should be the last throw for this message - if RecoveryPolicy is properly set the message will be sent over to error queue
                throw;
            }
        }
    }
}
