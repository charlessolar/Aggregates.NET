using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus;
using NServiceBus.Pipeline;

namespace Aggregates.Internal
{
    internal class TimeExecutionBehavior : Behavior<IIncomingPhysicalMessageContext>
    {
        private static readonly ILog Logger = LogProvider.GetLogger("TimeExecutionBehavior");
        private static readonly ILog SlowLogger = LogProvider.GetLogger("Slow Alarm");
        private static readonly HashSet<string> SlowCommandTypes = new HashSet<string>();
        private static readonly object SlowLock = new object();
        private readonly TimeSpan _slowAlert;

        public TimeExecutionBehavior(TimeSpan slowAlertThreshold)
        {
            _slowAlert = slowAlertThreshold;
        }

        public override async Task Invoke(IIncomingPhysicalMessageContext context, Func<Task> next)
        {
            var verbose = false;

            string messageTypeIdentifier;
            if (!context.MessageHeaders.TryGetValue(Headers.EnclosedMessageTypes, out messageTypeIdentifier))
                messageTypeIdentifier = "<UNKNOWN>";
            messageTypeIdentifier += ",";
            messageTypeIdentifier = messageTypeIdentifier.Substring(0, messageTypeIdentifier.IndexOf(','));

            try
            {
                if (SlowCommandTypes.Contains(messageTypeIdentifier))
                {
                    lock (SlowLock) SlowCommandTypes.Remove(messageTypeIdentifier);
                    Defaults.MinimumLogging.Value = LogLevel.Debug;
                    verbose = true;
                }
                
                var start = Stopwatch.GetTimestamp();

                await next().ConfigureAwait(false);

                var end = Stopwatch.GetTimestamp();
                var elapsed = (end - start) * (1.0 / Stopwatch.Frequency) * 1000;

                if (elapsed > _slowAlert.TotalSeconds)
                {
                    SlowLogger.WarnEvent("Processed", "{MessageId} {MessageType} took {Milliseconds} payload {Payload}", context.MessageId, messageTypeIdentifier, elapsed, Encoding.UTF8.GetString(context.Message.Body).MaxLines(10));
                    if (!verbose)
                        lock (SlowLock) SlowCommandTypes.Add(messageTypeIdentifier);
                }

            }
            finally
            {
                if (verbose)
                    Defaults.MinimumLogging.Value = null;
            }
        }
    }
}
