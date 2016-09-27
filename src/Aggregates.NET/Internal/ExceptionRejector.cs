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
        private readonly ReadOnlySettings _settings;
        private readonly Int32 _maxRetries;

        public ExceptionRejector(ReadOnlySettings settings)
        {
            _settings = settings;
            _maxRetries = _settings.Get<Int32>("MaxRetries");
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var messageId = context.MessageId;
            Int32 existingRetry = 0;
            try
            {
                await next();
                _retryRegistry.TryRemove(messageId, out existingRetry);
            }
            catch (Exception e)
            {
                _retryRegistry.TryGetValue(messageId, out existingRetry);
                                
                if (existingRetry < _maxRetries || _maxRetries == -1)
                {
                    Logger.WriteFormat(LogLevel.Warn, "Message {0} has faulted! {1}/{2} times\nException: {3}\nHeaders: {4}\nBody: {5}", context.MessageId, existingRetry, _maxRetries, e, context.MessageHeaders, Encoding.UTF8.GetString(context.Message.Body));
                    _retryRegistry[messageId] = existingRetry + 1;
                    throw;
                }

                // At this point message is dead - should be moved to error queue, send message to client that their request was rejected due to error 
                _errorsMeter.Mark();

                // Only send reply if the message is a SEND, else we risk endless reply loops as message failures bounce back and forth
                var intent = (MessageIntentEnum)Enum.Parse(typeof(MessageIntentEnum), context.Message.Headers[Headers.MessageIntent], true);
                if (intent != MessageIntentEnum.Send) return;

                // Tell the sender the command was not handled due to a service exception
                var rejection = context.Builder.Build<Func<Exception, String, Error>>();

                Logger.WriteFormat(LogLevel.Warn, "Message {0} has failed after {1} retries!\nException: {2}\nHeaders: {3}\nBody: {4}", context.MessageId, existingRetry, e, context.MessageHeaders, Encoding.UTF8.GetString(context.Message.Body));

                // Wrap exception in our object which is serializable
                await context.Reply(rejection(e, $"Rejected message after {existingRetry} retries!\n Payload: {Encoding.UTF8.GetString(context.Message.Body)}"));

                // Should be the last throw for this message - if RecoveryPolicy is properly set the message will be sent over to error queue
                throw;
            }
        }
    }
}
