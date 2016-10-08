using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Extensions;
using Aggregates.Messages;
using Metrics;
using Newtonsoft.Json;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class ExceptionRejector : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ExceptionRejector));

        private static readonly ConcurrentDictionary<string, int> RetryRegistry = new ConcurrentDictionary<string, int>();
        private static readonly Meter ErrorsMeter = Metric.Meter("Message Faults", Unit.Errors);
        private readonly int _immediate;
        private readonly bool _forever;

        public ExceptionRejector(int immediateRetries, bool forever)
        {
            _immediate = immediateRetries;
            _forever = forever;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var messageId = context.MessageId;
            var attempts = 1;
            try
            {
                RetryRegistry.TryRemove(messageId, out attempts);
                context.Extensions.Set(Defaults.Attempts, attempts);

                await next().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (attempts < _immediate)
                {
                    Logger.WriteFormat(LogLevel.Warn, $"Message {context.MessageId} has faulted! {attempts}/{_immediate} times\nException: {e.GetType().FullName}\nHeaders: {JsonConvert.SerializeObject(context.MessageHeaders, Formatting.None)}\nBody: {Encoding.UTF8.GetString(context.Message.Body)}");
                    RetryRegistry.TryAdd(messageId, attempts + 1);
                    throw;
                }
                // At this point message is dead - should be moved to error queue, send message to client that their request was rejected due to error 
                ErrorsMeter.Mark();

                // They've chosen to never move to error queue so don't reply with error
                if (_forever)
                    throw;

                // Only send reply if the message is a SEND, else we risk endless reply loops as message failures bounce back and forth
                var intent = (MessageIntentEnum)Enum.Parse(typeof(MessageIntentEnum), context.Message.Headers[Headers.MessageIntent], true);
                if (intent != MessageIntentEnum.Send) return;


                Logger.WriteFormat(LogLevel.Warn, $"Message {context.MessageId} has failed after {attempts} retries!\nException: {e.GetType().FullName}\nHeaders: {JsonConvert.SerializeObject(context.MessageHeaders, Formatting.None)}\nBody: {Encoding.UTF8.GetString(context.Message.Body)}");
                
                // Only need to reply if the client expects it
                if (!context.Message.Headers.ContainsKey(Defaults.RequestResponse) || context.Message.Headers[Defaults.RequestResponse] != "1")
                    throw;

                // Tell the sender the command was not handled due to a service exception
                var rejection = context.Builder.Build<Func<Exception, string, IError>>();
                // Wrap exception in our object which is serializable
                await context.Reply(rejection(e, $"Rejected message after {attempts} retries!\n Payload: {Encoding.UTF8.GetString(context.Message.Body)}")).ConfigureAwait(false);

                // Should be the last throw for this message - if RecoveryPolicy is properly set the message will be sent over to error queue
                throw;
            }
        }
    }
}
