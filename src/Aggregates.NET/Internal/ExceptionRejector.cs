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
            var messageId = context.PhysicalMessage.Id;
            try
            {
                if(_retryRegistry.ContainsKey(messageId))
                    context.PhysicalMessage.Headers[Headers.Retries] = _retryRegistry[messageId].ToString();

                next();
                _retryRegistry.Remove(context.PhysicalMessage.Id);
            }
            catch (Exception e)
            {
                var numberOfRetries = 0;
                _retryRegistry.TryGetValue(messageId, out numberOfRetries);
                    
                if (numberOfRetries < _maxRetries)
                {
                    Logger.WarnFormat("Message {2} type {0} has faulted! {1} times", context.IncomingLogicalMessage.MessageType.FullName, numberOfRetries, context.PhysicalMessage.Id);
                    _retryRegistry[messageId] = numberOfRetries + 1;
                    Thread.Sleep(75);
                    throw;
                }
                _retryRegistry.Remove(messageId);


                _errorsMeter.Mark();
                try
                {
                    Logger.ErrorFormat("Message {2} type {0} has faulted!\nHeaders: {3}\nPayload: {4}\nException: {1}", context.IncomingLogicalMessage.MessageType.FullName, e, context.PhysicalMessage.Id, JsonConvert.SerializeObject(context.PhysicalMessage.Headers), JsonConvert.SerializeObject(context.IncomingLogicalMessage.Instance));
                    // Tell the sender the command was not handled due to a service exception
                    var rejection = context.Builder.Build<Func<Exception, String, Error>>();
                    // Wrap exception in our object which is serializable
                    _bus.Reply(rejection(e, $"Rejected message {context.IncomingLogicalMessage.MessageType.FullName}\n Payload: {JsonConvert.SerializeObject(context.IncomingLogicalMessage.Instance)}"));
                }
                catch (KeyNotFoundException)
                {
                    Logger.ErrorFormat("Message {1} [Unknown] has faulted!\nHeaders: {2}\nException: {0}", e, context.PhysicalMessage.Id, JsonConvert.SerializeObject(context.PhysicalMessage.Headers));
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
