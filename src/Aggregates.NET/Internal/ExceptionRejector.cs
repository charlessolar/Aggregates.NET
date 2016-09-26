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

namespace Aggregates.Internal
{
    internal class ExceptionRejector : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ExceptionRejector));

        private static IDictionary<String, Int32> _retryRegistry = new Dictionary<String, Int32>();
        private static Meter _errorsMeter = Metric.Meter("Message Faults", Unit.Errors);
        private readonly ReadOnlySettings _settings;
        private readonly Int32 _maxRetries;

        public ExceptionRejector(ReadOnlySettings settings)
        {
            _settings = settings;
            _maxRetries = _settings.Get<Int32>("MaxRetries");
        }

        public override Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var messageId = context.MessageId;
            try
            {
                return next();
            }
            catch (Exception e)
            {
                var numberOfRetries = "";
                context.MessageHeaders.TryGetValue(Defaults.RETRY_HEADER, out numberOfRetries);
                if (String.IsNullOrEmpty(numberOfRetries))
                    throw;

                var retries = Int32.Parse(numberOfRetries);
                if (retries < _maxRetries || _maxRetries == -1)
                {
                    Logger.WriteFormat(LogLevel.Warn, "Message {0} has faulted! {1}/{2} times\nException: {3}\nHeaders: {4}\nBody: {5}", context.MessageId, retries, _maxRetries, e, context.MessageHeaders, Encoding.UTF8.GetString(context.Message.Body));
                    throw;
                }

                // At this point message is dead - should be moved to error queue, send message to client that their request was rejected due to error 
                _errorsMeter.Mark();

                // Only send reply if the message is a SEND, else we risk endless reply loops as message failures bounce back and forth
                var intent = (MessageIntentEnum)Enum.Parse(typeof(MessageIntentEnum), context.Message.Headers[Headers.MessageIntent], true);
                if (intent != MessageIntentEnum.Send) return Task.CompletedTask;

                // Tell the sender the command was not handled due to a service exception
                var rejection = context.Builder.Build<Func<Exception, String, Error>>();

                Logger.WriteFormat(LogLevel.Warn, "Message {0} has failed after {1} retries!\nException: {2}\nHeaders: {3}\nBody: {4}", context.MessageId, retries, e, context.MessageHeaders, Encoding.UTF8.GetString(context.Message.Body));

                // Wrap exception in our object which is serializable
                return context.Reply(rejection(e, $"Rejected message after {retries} retries!\n Payload: {Encoding.UTF8.GetString(context.Message.Body)}"));
            }
        }
    }
}
