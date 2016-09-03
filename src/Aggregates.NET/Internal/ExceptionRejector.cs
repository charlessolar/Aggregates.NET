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
    internal class ExceptionRejector : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ExceptionRejector));

        private static IDictionary<String, Int32> _retryRegistry = new Dictionary<String, Int32>();
        private static Meter _errorsMeter = Metric.Meter("Message Faults", Unit.Errors);
        private readonly IBus _bus;
        private readonly ReadOnlySettings _settings;
        private readonly Int32 _maxRetries;

        public ExceptionRejector(IBus bus, ReadOnlySettings settings)
        {
            _bus = bus;
            _settings = settings;
            _maxRetries = _settings.Get<Int32>("MaxRetries");
        }

        public void Invoke(IncomingContext context, Action next)
        {
            Invoke(new IncomingContextWrapper(context), next);
        }

        public void Invoke(IContextAccessor context, Action next)
        {
            var messageId = context.PhysicalMessageId;
            try
            {
                if (_retryRegistry.ContainsKey(messageId))
                {
                    context.SetPhysicalMessageHeader(Headers.Retries,  _retryRegistry[messageId].ToString());
                    context.Set<Int32>("AggregatesNet.Retries", _retryRegistry[messageId]);
                }

                next();
                _retryRegistry.Remove(context.PhysicalMessageId);
            }
            catch (Exception e)
            {
                var numberOfRetries = 0;
                _retryRegistry.TryGetValue(messageId, out numberOfRetries);

                if (numberOfRetries < _maxRetries || _maxRetries == -1)
                {
                    try
                    {
                        Logger.WriteFormat(LogLevel.Warn, "Message {3} type [{0}] has faulted! {1}/{2} times\nBody: {4}", context.IncomingLogicalMessageMessageType.FullName, numberOfRetries, _maxRetries, context.PhysicalMessageId, Encoding.UTF8.GetString(context.PhysicalMessageBody));
                    }
                    catch (KeyNotFoundException)
                    {
                        Logger.WriteFormat(LogLevel.Warn, "Message {3} type [{0}] has faulted! {1}/{2} times\nBody: {4}", "UNKNOWN", numberOfRetries, _maxRetries, context.PhysicalMessageId, Encoding.UTF8.GetString(context.PhysicalMessageBody));
                    }
                    _retryRegistry[messageId] = numberOfRetries + 1;
                    Thread.Sleep(75 * (numberOfRetries / 2));
                    throw;
                }
                _retryRegistry.Remove(messageId);

                _errorsMeter.Mark();

                // Only send reply if the message is a SEND, else we risk endless reply loops as message failures bounce back and forth
                if (context.PhysicalMessageMessageIntent != MessageIntentEnum.Send) return;
                try
                {
                    Logger.WriteFormat(LogLevel.Error, "Message {2} type [{0}] has faulted!\nHeaders: {3}\nPayload: {4}\nException: {1}", context.IncomingLogicalMessageMessageType.FullName, e, context.PhysicalMessageId, JsonConvert.SerializeObject(context.PhysicalMessageHeaders), JsonConvert.SerializeObject(context.IncomingLogicalMessageInstance));
                    // Tell the sender the command was not handled due to a service exception
                    var rejection = context.Builder.Build<Func<Exception, String, Error>>();
                    // Wrap exception in our object which is serializable
                    _bus.Reply(rejection(e, $"Rejected message {context.IncomingLogicalMessageMessageType.FullName}\n Payload: {JsonConvert.SerializeObject(context.IncomingLogicalMessageInstance)}"));
                }
                catch (KeyNotFoundException)
                {
                    Logger.WriteFormat(LogLevel.Error, "Message {1} type [Unknown] has faulted!\nHeaders: {2}\nException: {0}", e, context.PhysicalMessageId, JsonConvert.SerializeObject(context.PhysicalMessageHeaders));
                }
            }

        }
    }

    internal class ExceptionRejectorRegistration : RegisterStep
    {
        public ExceptionRejectorRegistration()
            : base("ExceptionRejector", typeof(ExceptionRejector), "Catches exceptions thrown while processing and reports to client via IReject")
        {
            InsertBefore(WellKnownStep.CreateChildContainer);

        }
    }
}
