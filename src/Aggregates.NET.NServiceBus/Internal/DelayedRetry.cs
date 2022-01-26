using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Microsoft.Extensions.Logging;
using NServiceBus;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    public class DelayedRetry
    {
        private readonly ILogger Logger;
        private readonly IMetrics _metrics;
        private readonly IMessageDispatcher _dispatcher;

        public DelayedRetry(ILoggerFactory logFactory, IMetrics metrics, IMessageDispatcher dispatcher)
        {
            Logger = logFactory.CreateLogger("DelayedRetry");
            _metrics = metrics;
            _dispatcher = dispatcher;
        }

        public virtual void QueueRetry(IFullMessage message, TimeSpan delay)
        {
            _metrics.Increment("Retry Queue", Unit.Message);
            var messageId = Guid.NewGuid().ToString();
            message.Headers.TryGetValue(Headers.MessageId, out messageId);

            Timer.Expire(Logger, (state) =>
            {
                var msg = (IFullMessage)state;
                
                _metrics.Decrement("Retry Queue", Unit.Message);
                return _dispatcher.SendLocal(msg);
            }, message, delay, $"message {messageId}");
        }        
    }
}
