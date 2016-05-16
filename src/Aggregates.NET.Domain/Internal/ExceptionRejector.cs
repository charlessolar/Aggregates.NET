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

namespace Aggregates.Internal
{
    internal class ExceptionRejector : IBehavior<IncomingContext>
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(ExceptionRejector));

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
            try
            {
                next();
            }
            catch (Exception e)
            {
                if (GetNumberOfFirstLevelRetries(context.PhysicalMessage) < _maxRetries)
                {
                    Logger.WarnFormat("Message {2} type {0} has faulted! {1} times", context.IncomingLogicalMessage.MessageType.FullName, GetNumberOfFirstLevelRetries(context.PhysicalMessage), context.PhysicalMessage.Id);
                    throw;
                }
                

                _errorsMeter.Mark();
                try
                {
                    Logger.WarnFormat("Message {2} type {0} has faulted!\nException: {1}", context.IncomingLogicalMessage.MessageType.FullName, e, context.PhysicalMessage.Id);
                }
                catch (KeyNotFoundException)
                {
                    Logger.WarnFormat("Message {1} [Unknown] has faulted!\nException: {0}", e, context.PhysicalMessage.Id);
                }
                // Tell the sender the command was not handled due to a service exception
                var rejection = context.Builder.Build<Func<Exception, String, Error>>();
                // Wrap exception in our object which is serializable
                _bus.Reply(rejection(e, $"Rejected message {context.IncomingLogicalMessage.MessageType.FullName}\n Payload: {JsonConvert.SerializeObject(context.IncomingLogicalMessage.Instance)}"));
            }

        }
        static int GetNumberOfFirstLevelRetries(TransportMessage message)
        {
            string value;
            if (message.Headers.TryGetValue(Headers.FLRetries, out value))
            {
                int i;
                if (int.TryParse(value, out i))
                {
                    return i;
                }
            }
            return 0;
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
